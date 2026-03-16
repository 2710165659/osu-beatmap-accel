using System;
using System.Linq;
using System.Reflection;

namespace osu.Game.Rulesets.BeatmapAccel.Features.Injection.Utils;

public static class ReflectionExtensions
{
    private const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static FieldInfo? FindFieldInstanceAssignable(this object obj, Type type)
        => findField(obj.GetType(), type);

    public static object? FindInstanceAssignable(this object obj, Type type)
        => obj.FindFieldInstanceAssignable(type)?.GetValue(obj);

    private static FieldInfo? findField(Type? type, Type targetType)
    {
        if (type == null)
            return null;

        FieldInfo? field = type.GetFields(flags).FirstOrDefault(f => targetType.IsAssignableFrom(f.FieldType));
        return field ?? findField(type.BaseType, targetType);
    }
}
