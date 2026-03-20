using System;

namespace SDKGeneratorBNM
{
    public static class Config
    {
        public enum NamingStyle
        {
            GetSet,
            GetterSetter,
        }

        public enum MethodStyle
        {
            Wrapper, // GetField(), SetField(), method.Call()
            Accessor, // field->Get(), field->Set(), method->Call()
        }

        public static NamingStyle MethodNamingStyle = NamingStyle.GetSet;
        public static MethodStyle MethodAccessorStyle = MethodStyle.Wrapper;
        public static bool UseBNMResolve = false; // Use BNMResolve.hpp for (GameObject, Text, etc.) or fallback to Il2CppObject
        public static string OutputDir = "SDK";
        public static bool SingleFile = false;

        public static string FormatGetterName(string name)
        {
            string n = Utils.FormatInvalidName(name);
            return MethodNamingStyle == NamingStyle.GetSet ? $"Get{Utils.ToPascalCase(n)}" : $"get_{Utils.ToCamelCase(n)}";
        }

        public static string FormatSetterName(string name)
        {
            string n = Utils.FormatInvalidName(name);
            return MethodNamingStyle == NamingStyle.GetSet ? $"Set{Utils.ToPascalCase(n)}" : $"set_{Utils.ToCamelCase(n)}";
        }

        public static string GetPropertyMethodName(string propertyName, bool isGetter) => isGetter ? $"get_{propertyName}" : $"set_{propertyName}";
    }
}
