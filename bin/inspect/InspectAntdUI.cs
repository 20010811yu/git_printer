using System;
using System.Linq;
using System.Reflection;

// 临时工具：反射确认 AntdUI Table CellClick 事件的真实委托与参数（用完即删）
class Program
{
    static void Main()
    {
        var asm = Assembly.LoadFrom(@"C:\Users\20010811\.nuget\packages\antdui\2.4.7\lib\net8.0-windows7.0\AntdUI.dll");
        var table = asm.GetExportedTypes().First(t => t.FullName == "AntdUI.Table");

        // 从 Table 类型本身取事件的真实委托类型（不按名字全局搜索，避免命中 Chat 组件同名委托）
        foreach (var evtName in new[] { "CellClick", "CellFocused", "CellClickBegin", "SelectIndexChanged" })
        {
            var evt = table.GetEvent(evtName);
            if (evt == null) { Console.WriteLine(evtName + ": not found"); continue; }
            var invoke = evt.EventHandlerType.GetMethod("Invoke");
            Console.WriteLine("Table." + evtName + " -> " + evt.EventHandlerType.FullName);
            foreach (var p in invoke.GetParameters())
                Console.WriteLine("    param " + p.Name + " : " + p.ParameterType.FullName);
            var paraType = invoke.GetParameters().LastOrDefault()?.ParameterType;
            if (paraType != null && !paraType.IsValueType)
            {
                foreach (var prop in paraType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    Console.WriteLine("      prop " + prop.Name + " : " + prop.PropertyType.Name);
            }
            Console.WriteLine();
        }

        // 事件参数类型继承链（确认是否为公共基类）
        var clickEvt = table.GetEvent("CellClick");
        if (clickEvt != null)
        {
            var t = clickEvt.EventHandlerType.GetMethod("Invoke").GetParameters().Last().ParameterType;
            Console.WriteLine("CellClick args inheritance: " + t.FullName);
            while (t.BaseType != null && t.BaseType != typeof(object))
            {
                t = t.BaseType;
                Console.WriteLine("  <- " + t.FullName);
            }
        }
    }
}