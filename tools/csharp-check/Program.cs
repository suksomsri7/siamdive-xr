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

// ── 1) syntax ────────────────────────────────────────────────────────────────
foreach (string file in files)
{
    string text = File.ReadAllText(file);
    SyntaxTree tree = CSharpSyntaxTree.ParseText(
        SourceText.From(text),
        new CSharpParseOptions(LanguageVersion.CSharp9));   // Unity 6000.0 = C# 9
    trees.Add((file, tree));

    foreach (Diagnostic d in tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error))
    {
        Report(file, d.Location, d.Id, d.GetMessage());
        problems++;
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

Console.WriteLine($"csharp-check: {files.Length} files · {problems} problem(s)");
return problems == 0 ? 0 : 1;

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
