using System.Reflection;

// NUnitLite console runner — ตัวเดียวกับที่ Unity ใช้ (NUnit 3), แค่ไม่ต้องมี Editor.
public static class CoreTestMain
{
    public static int Main(string[] args)
        => new NUnitLite.AutoRun(Assembly.GetExecutingAssembly()).Execute(args);
}
