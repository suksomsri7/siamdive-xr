// ─────────────────────────────────────────────────────────────────────────────
// UnityEngine.Vector3 พอให้ ItemPicker.cs คอมไพล์บนเครื่องนี้ได้
//
// ทำไมต้องมี: FleeMath.DiverPanicSpeed อ่าน DroneFlight.Speed → DroneFlight อ่าน
// ItemPicker.UnitsPerMetre → ItemPicker ใช้ Vector3 เป็นแค่ "ถุงใส่ x,y,z" ไม่ได้
// เรียก Mathf/Debug อะไรเลย. shim นี้จึงเป็นแค่ struct 3 ตัวเลข ไม่ใช่การจำลอง Unity
// และไม่มีตัวเลขของ DiveMap อยู่ในนี้เลย (ค่าคงที่ทุกตัวยังมาจากไฟล์จริงใน Assets/).
//
// ห้ามโตไปกว่านี้ — ถ้าไฟล์ Core ไหนต้องใช้ Unity มากกว่านี้ แปลว่ามันไม่ใช่ pure logic
// และต้องรอ CI เหมือนเดิม.
// ─────────────────────────────────────────────────────────────────────────────
namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator *(Vector3 a, float k) => new Vector3(a.x * k, a.y * k, a.z * k);
        public override string ToString() => $"({x}, {y}, {z})";
    }
}
