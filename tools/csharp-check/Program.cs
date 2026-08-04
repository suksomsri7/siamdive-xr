using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

// ─────────────────────────────────────────────────────────────────────────────
// csharp-check — catch on this machine the compile errors that would otherwise
// cost a 15-minute CI round.
//
// It does NOT have Unity's assemblies, so it cannot type-check. What it does:
//   1. Roslyn PARSE of every source file → every syntax error the compiler would
//      report (missing brace/semicolon/paren, malformed generic, bad interpolation).
//   2. A scope walker that reproduces CS0136 / CS0128 — a local declared twice in
//      the same method, or shadowing one from an enclosing scope. This is the exact
//      error that turned CI run 30596171135 red for one variable named `card`.
//   3. `using` directives that point at namespaces no file in the project declares
//      AND that are not on a known-external allowlist — catches a typo'd namespace.
//
// Exit code 0 = clean, 1 = findings. Run it before every push:
//     dotnet run --project tools/csharp-check -- DiveMap/Assets
// ─────────────────────────────────────────────────────────────────────────────

string root = args.Length > 0 ? args[0] : "DiveMap/Assets";
if (!Directory.Exists(root))
{
    Console.Error.WriteLine($"csharp-check: no such directory: {root}");
    return 2;
}

string[] files = Directory
    .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Library{Path.DirectorySeparatorChar}") &&
                !f.Contains($"{Path.DirectorySeparatorChar}Temp{Path.DirectorySeparatorChar}"))
    .OrderBy(f => f, StringComparer.Ordinal)
    .ToArray();

var trees = new List<(string Path, SyntaxTree Tree)>();
int problems = 0;

// ── 1) syntax, once per platform ─────────────────────────────────────────────
// 🔴 Parsing with no symbols defined only ever compiles the #else side of the file, and a file
// can be perfectly balanced there while being broken on the other. That is not hypothetical:
// Editor/CameraUsageDeclaration.cs closed its namespace INSIDE `#if UNITY_ANDROID`, so every iOS
// build from 31 Jul was green and the first Android player build in weeks died on `CS1022` at the
// last line of a file nobody had edited. The Android player is only built on a hand-fired run, so
// three weeks passed between the mistake and the compiler seeing it.
//
// Each symbol set below is one real build of this project. A file is only clean when it parses
// under all of them, and the failure names the platform so it can be reproduced.
var platforms = new (string Name, string[] Symbols)[]
{
    ("default",       Array.Empty<string>()),
    ("UNITY_ANDROID", new[] { "UNITY_ANDROID" }),
    ("UNITY_IOS",     new[] { "UNITY_IOS", "UNITY_IPHONE" }),
    ("UNITY_EDITOR",  new[] { "UNITY_EDITOR" }),
};

foreach (string file in files)
{
    string text = File.ReadAllText(file);
    SourceText source = SourceText.From(text);

    for (int p = 0; p < platforms.Length; p++)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.CSharp9)   // Unity 6000.0 = C# 9
                .WithPreprocessorSymbols(platforms[p].Symbols));

        // Later passes want one tree per file; the symbol-free parse is the one they knew.
        if (p == 0) trees.Add((file, tree));

        foreach (Diagnostic d in tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error))
        {
            Report(file, d.Location, d.Id, $"[{platforms[p].Name}] {d.GetMessage()}");
            problems++;
        }
    }
}

// ── 2) local-variable collisions (CS0136 / CS0128) ───────────────────────────
foreach ((string path, SyntaxTree tree) in trees)
{
    foreach (SyntaxNode body in tree.GetRoot().DescendantNodes()
                 .Where(n => n is MethodDeclarationSyntax or LocalFunctionStatementSyntax or
                                  ConstructorDeclarationSyntax or AccessorDeclarationSyntax or
                                  ParenthesizedLambdaExpressionSyntax or SimpleLambdaExpressionSyntax))
    {
        SyntaxNode block = body switch
        {
            MethodDeclarationSyntax m => (SyntaxNode)m.Body ?? m.ExpressionBody,
            LocalFunctionStatementSyntax l => (SyntaxNode)l.Body ?? l.ExpressionBody,
            ConstructorDeclarationSyntax c => (SyntaxNode)c.Body ?? c.ExpressionBody,
            AccessorDeclarationSyntax a => (SyntaxNode)a.Body ?? a.ExpressionBody,
            ParenthesizedLambdaExpressionSyntax p => p.Body,
            SimpleLambdaExpressionSyntax s => s.Body,
            _ => null,
        };
        if (block == null) continue;

        // Parameters of the method itself occupy the outermost scope.
        var outer = new HashSet<string>(StringComparer.Ordinal);
        foreach (ParameterSyntax p in Parameters(body)) if (p.Identifier.ValueText.Length > 0) outer.Add(p.Identifier.ValueText);

        problems += WalkScope(path, block, new List<HashSet<string>> { outer });
    }
}

// ── 3) using directives that resolve to nothing ──────────────────────────────
var declared = new HashSet<string>(StringComparer.Ordinal);
foreach ((_, SyntaxTree tree) in trees)
    foreach (BaseNamespaceDeclarationSyntax ns in tree.GetRoot().DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
    {
        // Register every ancestor too: `namespace A.B.C` makes `A` and `A.B` usable.
        string full = ns.Name.ToString();
        string[] parts = full.Split('.');
        for (int i = 1; i <= parts.Length; i++) declared.Add(string.Join(".", parts.Take(i)));
    }

// Namespaces that come from outside this repo (Unity, the BCL, packages).
string[] externalPrefixes =
{
    "System", "UnityEngine", "UnityEditor", "Unity", "NUnit", "Newtonsoft",
    "GLTFast", "Microsoft", "TMPro", "JetBrains",
    // KTX for Unity (com.unity.cloud.ktx). Its assembly is called Ktx and its namespace is
    // KtxUnity — the mismatch is the package's, not a typo, so the prefix has to be spelled out
    // rather than caught by the "Unity" entry above.
    "KtxUnity",
};

foreach ((string path, SyntaxTree tree) in trees)
    foreach (UsingDirectiveSyntax u in tree.GetRoot().DescendantNodes().OfType<UsingDirectiveSyntax>())
    {
        if (u.Alias != null || u.StaticKeyword.ValueText.Length > 0) continue;
        string name = u.Name?.ToString();
        if (string.IsNullOrEmpty(name)) continue;
        if (declared.Contains(name)) continue;
        if (externalPrefixes.Any(p => name == p || name.StartsWith(p + ".", StringComparison.Ordinal))) continue;

        Report(path, u.GetLocation(), "CHK001", $"using '{name}' — no file in this project declares that namespace");
        problems++;
    }

// ── 4) project types used from a namespace that cannot see them (CS0103) ─────
//
// The compiler needs references to type-check; this does not. But it CAN check the one
// case that keeps happening: a type declared IN THIS PROJECT, referenced by its simple
// name, from a file whose namespace + usings do not reach it.
//
// Real example this was written for — MapEditor.cs (namespace DiveMap.Runtime) calling
// Toast (DiveMap.Runtime.Ui). A CHILD namespace is not in scope; the file needed
// `using DiveMap.Runtime.Ui;`. Cost: one CI round.
//
// Deliberately conservative: a name is only reported when it is declared exactly once in
// the project (no ambiguity about which namespace it should have come from), it is not
// declared anywhere the file can already see, and it appears as a bare identifier.
var typeNamespaces = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
foreach ((_, SyntaxTree tree) in trees)
    foreach (BaseTypeDeclarationSyntax type in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
    {
        // Nested types are reached through their parent's name, not on their own.
        if (type.Parent is BaseTypeDeclarationSyntax) continue;
        string ns = NamespaceOf(type);
        if (!typeNamespaces.TryGetValue(type.Identifier.ValueText, out HashSet<string> set))
            typeNamespaces[type.Identifier.ValueText] = set = new HashSet<string>(StringComparer.Ordinal);
        set.Add(ns);
    }

foreach ((string path, SyntaxTree tree) in trees)
{
    SyntaxNode unit = tree.GetRoot();

    // Namespaces this file can see: its own and every ancestor, plus its usings.
    var fileUsings = new HashSet<string>(StringComparer.Ordinal);
    foreach (UsingDirectiveSyntax u in unit.DescendantNodes().OfType<UsingDirectiveSyntax>())
        if (u.Alias == null && u.Name != null) fileUsings.Add(u.Name.ToString());

    var reported = new HashSet<string>(StringComparer.Ordinal);

    foreach (IdentifierNameSyntax id in unit.DescendantNodes().OfType<IdentifierNameSyntax>())
    {
        string name = id.Identifier.ValueText;
        if (reported.Contains(name)) continue;
        if (!typeNamespaces.TryGetValue(name, out HashSet<string> declaredIn)) continue;
        if (declaredIn.Count != 1) continue;                       // ambiguous — leave it to the compiler

        // Only a bare `Name.Member` / `Name x` usage; `A.Name` is already qualified.
        if (id.Parent is QualifiedNameSyntax) continue;
        if (id.Parent is MemberAccessExpressionSyntax ma && ma.Name == id) continue;

        // Where does this occurrence sit?
        string here = NamespaceOf(id);
        string target = System.Linq.Enumerable.First(declaredIn);
        if (target.Length == 0) continue;                          // global namespace is always visible

        bool visible = fileUsings.Contains(target) ||
                       here == target ||
                       here.StartsWith(target + ".", StringComparison.Ordinal);   // ancestor of `here`

        if (visible) continue;

        Report(path, id.GetLocation(), "CS0103",
               $"'{name}' is declared in namespace '{target}', which this file cannot see — add `using {target};`");
        reported.Add(name);
        problems++;
    }
}

// ── 5) members that do not exist on a project type (CS0117 / CS0426 / CS1061) ─
//
// Written after CI went red on BOTH of these in one round:
//   GizmoController.cs(107): AppBoot.Manifest.Find(…)  → AssetManifest has Get(), not Find()
//   GizmoController.cs(310): WebCoord.Quat q          → Quat is a sibling type, not nested
//
// Scope: `Type.Member` where Type is declared in this project. Static members, nested types
// and enum values are all checkable from syntax alone. Instance chains are followed exactly
// ONE hop — `AppBoot.Manifest.Get(x)` works because Manifest's declared type is also a
// project type. Beyond that the guessing would start, so it stops.
var typeMembers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
var memberType = new Dictionary<string, string>(StringComparer.Ordinal);   // "Type.Member" → declared type
var partialTypes = new HashSet<string>(StringComparer.Ordinal);

foreach ((_, SyntaxTree tree) in trees)
    foreach (TypeDeclarationSyntax type in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
    {
        // NESTED types only ever appear as Outer.Nested, so a bare `Nested.Member` is almost
        // certainly a DIFFERENT type with the same short name — or not a type at all. Both false
        // positives this rule produced on its first run were exactly that:
        //   BuildResult.Succeeded  → UnityEditor.Build.Reporting.BuildResult, not SceneBuilder's
        //   Spec.Formation         → a property named Spec, not FishAssetPick's nested struct
        if (type.Parent is TypeDeclarationSyntax) continue;

        string owner = type.Identifier.ValueText;
        if (type.Modifiers.Any(m => m.ValueText == "partial")) partialTypes.Add(owner);
        if (!typeMembers.TryGetValue(owner, out HashSet<string> set))
            typeMembers[owner] = set = new HashSet<string>(StringComparer.Ordinal);

        foreach (MemberDeclarationSyntax m in type.Members)
        {
            switch (m)
            {
                case MethodDeclarationSyntax me: set.Add(me.Identifier.ValueText); break;
                case PropertyDeclarationSyntax pr:
                    set.Add(pr.Identifier.ValueText);
                    memberType[owner + "." + pr.Identifier.ValueText] = pr.Type.ToString();
                    break;
                case EventDeclarationSyntax ev: set.Add(ev.Identifier.ValueText); break;
                case FieldDeclarationSyntax fd:
                    foreach (VariableDeclaratorSyntax v in fd.Declaration.Variables)
                    {
                        set.Add(v.Identifier.ValueText);
                        memberType[owner + "." + v.Identifier.ValueText] = fd.Declaration.Type.ToString();
                    }
                    break;
                case EventFieldDeclarationSyntax efd:
                    foreach (VariableDeclaratorSyntax v in efd.Declaration.Variables) set.Add(v.Identifier.ValueText);
                    break;
                case BaseTypeDeclarationSyntax nested: set.Add(nested.Identifier.ValueText); break;
                case DelegateDeclarationSyntax dg: set.Add(dg.Identifier.ValueText); break;
            }
        }
        // A type with a base class may inherit anything — do not police it.
        if (type.BaseList != null) set.Add("*");
    }

foreach ((_, SyntaxTree tree) in trees)
    foreach (EnumDeclarationSyntax en in tree.GetRoot().DescendantNodes().OfType<EnumDeclarationSyntax>())
    {
        if (!typeMembers.TryGetValue(en.Identifier.ValueText, out HashSet<string> set))
            typeMembers[en.Identifier.ValueText] = set = new HashSet<string>(StringComparer.Ordinal);
        foreach (EnumMemberDeclarationSyntax v in en.Members) set.Add(v.Identifier.ValueText);
    }

var memberNamesAnywhere = new HashSet<string>(StringComparer.Ordinal);
foreach (KeyValuePair<string, HashSet<string>> kv in typeMembers)
    foreach (string m in kv.Value) memberNamesAnywhere.Add(m);

foreach ((string path, SyntaxTree tree) in trees)
    foreach (MemberAccessExpressionSyntax ma in tree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>())
    {
        string ownerName = null;

        if (ma.Expression is IdentifierNameSyntax owner)
        {
            ownerName = owner.Identifier.ValueText;
        }
        else if (ma.Expression is MemberAccessExpressionSyntax inner &&
                 inner.Expression is IdentifierNameSyntax root2)
        {
            // one hop: Type.Member.Next — resolve Member's declared type
            if (memberType.TryGetValue(root2.Identifier.ValueText + "." + inner.Name.Identifier.ValueText,
                                       out string declaredType))
                ownerName = declaredType;
        }
        if (ownerName == null) continue;
        if (!typeMembers.TryGetValue(ownerName, out HashSet<string> members)) continue;
        if (memberNamesAnywhere.Contains(ownerName)) continue;   // also a field/property name — ambiguous
        if (members.Contains("*") || partialTypes.Contains(ownerName)) continue;   // inherited / split across files

        string wanted = ma.Name.Identifier.ValueText;
        if (members.Contains(wanted)) continue;

        Report(path, ma.Name.GetLocation(), "CHK002",
               $"'{ownerName}' has no member '{wanted}' — did you mean one of: " +
               string.Join(", ", members.Where(x => x != "*").OrderBy(x => x, StringComparer.Ordinal).Take(8)) + "…");
        problems++;
    }

Console.WriteLine($"csharp-check: {files.Length} files · {problems} problem(s)");
return problems == 0 ? 0 : 1;

// Innermost namespace containing a node, or "" for the global namespace.
static string NamespaceOf(SyntaxNode node)
{
    for (SyntaxNode n = node.Parent; n != null; n = n.Parent)
        if (n is BaseNamespaceDeclarationSyntax ns) return ns.Name.ToString();
    return "";
}

// ─────────────────────────────────────────────────────────────────────────────

static IEnumerable<ParameterSyntax> Parameters(SyntaxNode node) => node switch
{
    MethodDeclarationSyntax m => m.ParameterList.Parameters,
    LocalFunctionStatementSyntax l => l.ParameterList.Parameters,
    ConstructorDeclarationSyntax c => c.ParameterList.Parameters,
    ParenthesizedLambdaExpressionSyntax p => p.ParameterList.Parameters,
    SimpleLambdaExpressionSyntax s => new[] { s.Parameter },
    _ => Enumerable.Empty<ParameterSyntax>(),
};

// Walk one scope, carrying the names visible from enclosing scopes. A declaration
// whose name is already visible is CS0136 (shadowing) or CS0128 (same scope) —
// the compiler rejects both, so both are reported.
static int WalkScope(string path, SyntaxNode scope, List<HashSet<string>> enclosing)
{
    int found = 0;
    var here = new HashSet<string>(StringComparer.Ordinal);
    var stack = new List<HashSet<string>>(enclosing) { here };

    foreach (SyntaxNode child in scope.ChildNodes())
        found += Visit(path, child, stack, here);

    return found;
}

static int Visit(string path, SyntaxNode node, List<HashSet<string>> stack, HashSet<string> here)
{
    int found = 0;

    switch (node)
    {
        // A nested lambda/local function starts its own method-level scope, and C#
        // DOES allow a lambda parameter to shadow… no: CS0136 applies there too.
        case LocalFunctionStatementSyntax:
        case ParenthesizedLambdaExpressionSyntax:
        case SimpleLambdaExpressionSyntax:
            return 0;   // handled as its own body by the outer loop

        case LocalDeclarationStatementSyntax decl:
            foreach (VariableDeclaratorSyntax v in decl.Declaration.Variables)
                found += Declare(path, v.Identifier, stack, here);
            return found;

        case ForStatementSyntax f:
        {
            var inner = new HashSet<string>(StringComparer.Ordinal);
            var s2 = new List<HashSet<string>>(stack) { inner };
            if (f.Declaration != null)
                foreach (VariableDeclaratorSyntax v in f.Declaration.Variables)
                    found += Declare(path, v.Identifier, stack, inner);
            if (f.Statement != null) found += WalkScope(path, f.Statement, s2);
            return found;
        }

        case ForEachStatementSyntax fe:
        {
            var inner = new HashSet<string>(StringComparer.Ordinal);
            var s2 = new List<HashSet<string>>(stack) { inner };
            found += Declare(path, fe.Identifier, stack, inner);
            if (fe.Statement != null) found += WalkScope(path, fe.Statement, s2);
            return found;
        }

        case UsingStatementSyntax us:
        {
            var inner = new HashSet<string>(StringComparer.Ordinal);
            var s2 = new List<HashSet<string>>(stack) { inner };
            if (us.Declaration != null)
                foreach (VariableDeclaratorSyntax v in us.Declaration.Variables)
                    found += Declare(path, v.Identifier, stack, inner);
            if (us.Statement != null) found += WalkScope(path, us.Statement, s2);
            return found;
        }

        case BlockSyntax b:
            return WalkScope(path, b, stack);

        default:
            // Any other statement (if/while/switch/try…) — recurse, treating a nested
            // block as a new scope and everything else as part of this one.
            foreach (SyntaxNode child in node.ChildNodes())
                found += Visit(path, child, stack, here);
            return found;
    }
}

static int Declare(string path, SyntaxToken id, List<HashSet<string>> stack, HashSet<string> target)
{
    string name = id.ValueText;
    if (string.IsNullOrEmpty(name)) return 0;

    foreach (HashSet<string> scope in stack)
    {
        if (!scope.Contains(name)) continue;
        Report(path, id.GetLocation(), "CS0136",
               $"a local named '{name}' is already in scope in this method — the compiler rejects this");
        return 1;
    }
    target.Add(name);
    return 0;
}

static void Report(string path, Location loc, string id, string message)
{
    FileLinePositionSpan span = loc.GetLineSpan();
    int line = span.StartLinePosition.Line + 1;
    int col = span.StartLinePosition.Character + 1;
    Console.WriteLine($"{path}({line},{col}): {id}: {message}");
}
