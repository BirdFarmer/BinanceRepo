using System;
using System.ComponentModel;
using System.Reflection;

namespace BinanceTestnet.Utilities
{
    public static class EnumExtensions
    {
        public static string GetDescription(this Enum value)
        {
            var fieldInfo = value.GetType().GetField(value.ToString());
            var descriptionAttribute = fieldInfo.GetCustomAttribute<DescriptionAttribute>();

            return descriptionAttribute == null ? value.ToString() : descriptionAttribute.Description;
        }
    }
}
