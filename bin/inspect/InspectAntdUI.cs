using System;
using System.Linq;
using System.Reflection;

// 临时工具：反射检查 AntdUI 编辑委托签名与 AntItem 类型（用完即删）
class Program
{
    static void Main()
    {
        var asm = Assembly.LoadFrom(@"C:\Users\20010811\.nuget\packages\antdui\2.4.7\lib\net8.0-windows7.0\AntdUI.dll");

        // 编辑相关委托签名
        string[] delegates = { "EndEditEventHandler", "EndValueEditEventHandler", "BeginEditEventHandler", "CellEditEnterEventHandler" };
        foreach (var name in delegates)
        {
            var dlg = asm.GetExportedTypes().FirstOrDefault(t => t.Name == name);
            if (dlg == null) { Console.WriteLine($"{name}: not found"); continue; }
            var invoke = dlg.GetMethod("Invoke");
            Console.WriteLine($"delegate {name}: {invoke}");
        }

        Console.WriteLine();

        // AntItem 类型细节（struct/class、字段、构造）
        var antItem = asm.GetExportedTypes().FirstOrDefault(t => t.Name == "AntItem");
        Console.WriteLine($"AntItem: {antItem?.FullName}, IsValueType={antItem?.IsValueType}");
        if (antItem != null)
        {
            foreach (var f in antItem.GetFields(BindingFlags.Public | BindingFlags.Instance)) Console.WriteLine($"  field {f}");
            foreach (var p in antItem.GetProperties(BindingFlags.Public | BindingFlags.Instance)) Console.WriteLine($"  prop {p}");
            foreach (var c in antItem.GetConstructors()) Console.WriteLine($"  ctor {c}");
        }

        Console.WriteLine();

        // TEditMode 枚举确认
        var editMode = asm.GetExportedTypes().First(t => t.Name == "TEditMode");
        Console.WriteLine($"TEditMode: {string.Join(", ", Enum.GetNames(editMode))}");
    }
}