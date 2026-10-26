using System;
using GameCore.HotUpdate;
using Il2CppInterop.Runtime;

namespace DataDump.Adapters;

/// <summary>
/// The game's singletons build themselves on first read. A world read before the game makes it is built before the
/// configs load, throws halfway through, and leaves the managers it already built answering the game's events for the
/// rest of the session. So the plugin only reads what the game already made.
/// </summary>
internal static class GameSingleton
{
    public static T? Existing<T>() where T : BaseSingleton<T>, new() =>
        IsCreated<T>() ? BaseSingleton<T>.Instance : null;

    /// <summary>Whether the game has made the singleton yet. Free to call every frame: nothing is allocated.</summary>
    public static bool IsCreated<T>() where T : BaseSingleton<T>, new() => Pointer<T>() != IntPtr.Zero;

    /// <summary>
    /// The singleton's native address, or zero before the game makes it. The game's own IsInstanceCreated only checks
    /// this field, but through interop each call boxes its answer, so the field is read directly.
    /// </summary>
    public static unsafe IntPtr Pointer<T>() where T : BaseSingleton<T>, new()
    {
        var field = InstanceField<T>.Field;

        if (field == IntPtr.Zero) return BaseSingleton<T>.IsInstanceCreated ? BaseSingleton<T>.Instance.Pointer : IntPtr.Zero;

        IntPtr instance;

        IL2CPP.il2cpp_field_static_get_value(field, &instance);

        return instance;
    }

    /// <summary>
    /// The static _Instance field, looked up once. The explicit static constructor keeps the lookup at the first read,
    /// never earlier: a method compiled ahead on a worker must not reach into the game.
    /// </summary>
    private static class InstanceField<T> where T : BaseSingleton<T>, new()
    {
        public static readonly IntPtr Field;

        static InstanceField()
        {
            try
            {
                Field = IL2CPP.GetIl2CppField(Il2CppClassPointerStore<BaseSingleton<T>>.NativeClassPtr, "_Instance");
            }
            catch (Exception)
            {
                // Without the field the interop getters still answer, only less cheaply.
                Field = IntPtr.Zero;
            }
        }
    }
}
