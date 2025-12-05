//using System.Collections.Concurrent;
//using System.Collections.Immutable;

//namespace Mavlink.Common;

///// <summary>
///// Generic bitmask for the <see cref="MavModeFlag"/> enum.
///// </summary>
///// <remarks>
///// <see cref="TUnderlying"/> can be any integer type (byte, sbyte, ushort, short, uint, int, ulong, long).
///// </remarks>
///// <example>
///// var bitmask = new MavModeFlagBitmask<byte>((byte)MavModeFlag.SafetyArmed | (byte)MavModeFlag.StabilizeEnabled);
///// </example>
//#if NET8_0_OR_GREATER
//internal readonly struct MavModeFlagBitmask<TUnderlying> : EnumBitmask.IEnumBitmask<MavModeFlag, TUnderlying>
//    where TUnderlying : struct, IBinaryInteger<TUnderlying>
//#else
//public readonly struct MavModeFlagBitmask<TUnderlying> : EnumBitmask.IEnumBitmask<MavModeFlag, TUnderlying>
//	where TUnderlying : struct
//#endif
//{
//	private readonly TUnderlying _bitmask;
//	private readonly Lazy<ImmutableArray<MavModeFlag>> _activeFlagsLazy;

//	private static readonly ConcurrentDictionary<TUnderlying, ImmutableArray<MavModeFlag>> _activeFlagsCache = new();

//	/// <summary>
//	/// Initializes a new instance of the <see cref="MavModeFlagBitmask{TUnderlying}"/> struct with the specified bitmask value.
//	/// Active flags are computed lazily on first access and cached for performance.
//	/// </summary>
//	/// <param name="bitmask">The bitmask value representing a combination of <see cref="MavModeFlag"/> flags.</param>
//	/// <exception cref="ArgumentOutOfRangeException">Thrown when the bitmask contains bits outside the valid range of <see cref="MavModeFlag"/>.</exception>
//	public MavModeFlagBitmask(TUnderlying bitmask)
//	{
//#if NET8_0_OR_GREATER
//        // MavModeFlag is a byte enum, so all flags fit within 0xFF.
//        if ((bitmask & ~TUnderlying.CreateTruncating(0xFF)) != TUnderlying.Zero)
//#else
//		if ((bitmask & ~(TUnderlying)Convert.ChangeType(0xFF, typeof(TUnderlying))) != default(TUnderlying))
//#endif
//		{
//			throw new ArgumentOutOfRangeException(nameof(bitmask), "Bitmask contains bits outside the range of MavModeFlag (byte).");
//		}

//#if NET8_0_OR_GREATER
//        _bitmask = bitmask & TUnderlying.CreateTruncating(0xFF);
//#else
//		_bitmask = bitmask & (TUnderlying)Convert.ChangeType(0xFF, typeof(TUnderlying));
//#endif
//		_activeFlagsLazy = new Lazy<ImmutableArray<MavModeFlag>>(
//			() => _activeFlagsCache.GetOrAdd(_bitmask, ComputeActiveFlags)
//		);
//	}

//	/// <summary>
//	/// Gets the underlying bitmask value representing the combination of enum flags.
//	/// </summary>
//	public TUnderlying Bitmask => _bitmask;

//	/// <summary>
//	/// Gets the <see cref="MavModeFlag"/> enum value interpreted from the bitmask.
//	/// The bitmask is cast to the enum's base type (byte) to ensure compatibility.
//	/// </summary>
//	public MavModeFlag Value
//	{
//		get
//		{
//#if NET8_0_OR_GREATER
//            return (MavModeFlag)TUnderlying.CreateTruncating(_bitmask);
//#else
//			return (MavModeFlag)Convert.ToByte(_bitmask);
//#endif
//		}
//	}

//	/// <summary>
//	/// Gets an immutable array of active <see cref="MavModeFlag"/> flags set in the bitmask.
//	/// The flags are computed on first access and cached locally for subsequent accesses to optimize performance.
//	/// </summary>
//	public ImmutableArray<MavModeFlag> ActiveFlags => _activeFlagsLazy.Value;

//	/// <summary>
//	/// Computes the active flags from the specified bitmask value.
//	/// </summary>
//	/// <param name="bitmask">The bitmask value to analyze.</param>
//	/// <returns>An immutable array containing the <see cref="MavModeFlag"/> flags that are set in the bitmask.</returns>
//	private ImmutableArray<MavModeFlag> ComputeActiveFlags(TUnderlying bitmask)
//	{
//#if NET8_0_OR_GREATER
//        if (bitmask == TUnderlying.Zero)
//        {
//            return ImmutableArray<MavModeFlag>.Empty;
//        }
//        int count = bitmask switch
//        {
//            byte b => BitOperations.PopCount(b),
//            sbyte sb => BitOperations.PopCount((byte)sb),
//            ushort us => BitOperations.PopCount(us),
//            short s => BitOperations.PopCount((ushort)s),
//            uint u => BitOperations.PopCount(u),
//            int i => BitOperations.PopCount((uint)i),
//            ulong ul => BitOperations.PopCount(ul),
//            long l => BitOperations.PopCount((ulong)l),
//            _ => throw new InvalidOperationException("Unsupported underlying type for PopCount.")
//        };
//        var builder = ImmutableArray.CreateBuilder<MavModeFlag>(count);

//        if ((bitmask & TUnderlying.CreateTruncating((byte)MavModeFlag.SafetyArmed)) != TUnderlying.Zero) builder.Add(MavModeFlag.SafetyArmed);
//        if ((bitmask & TUnderlying.CreateTruncating((byte)MavModeFlag.ManualInputEnabled)) != TUnderlying.Zero) builder.Add(MavModeFlag.ManualInputEnabled);
//        if ((bitmask & TUnderlying.CreateTruncating((byte)MavModeFlag.HilEnabled)) != TUnderlying.Zero) builder.Add(MavModeFlag.HilEnabled);
//        if ((bitmask & TUnderlying.CreateTruncating((byte)MavModeFlag.StabilizeEnabled)) != TUnderlying.Zero) builder.Add(MavModeFlag.StabilizeEnabled);
//        if ((bitmask & TUnderlying.CreateTruncating((byte)MavModeFlag.GuidedEnabled)) != TUnderlying.Zero) builder.Add(MavModeFlag.GuidedEnabled);
//        if ((bitmask & TUnderlying.CreateTruncating((byte)MavModeFlag.AutoEnabled)) != TUnderlying.Zero) builder.Add(MavModeFlag.AutoEnabled);
//        if ((bitmask & TUnderlying.CreateTruncating((byte)MavModeFlag.TestEnabled)) != TUnderlying.Zero) builder.Add(MavModeFlag.TestEnabled);
//        if ((bitmask & TUnderlying.CreateTruncating((byte)MavModeFlag.CustomModeEnabled)) != TUnderlying.Zero) builder.Add(MavModeFlag.CustomModeEnabled);

//        return builder.MoveToImmutable();
//#elif NETCOREAPP3_0_OR_GREATER
//        if (EqualityComparer<TUnderlying>.Default.Equals(bitmask, default))
//        {
//            return ImmutableArray<MavModeFlag>.Empty;
//        }
//        int count = bitmask switch
//        {
//            byte b => BitOperations.PopCount(b),
//            sbyte sb => BitOperations.PopCount((byte)sb),
//            ushort us => BitOperations.PopCount(us),
//            short s => BitOperations.PopCount((ushort)s),
//            uint u => BitOperations.PopCount(u),
//            int i => BitOperations.PopCount((uint)i),
//            ulong ul => BitOperations.PopCount(ul),
//            long l => BitOperations.PopCount((ulong)l),
//            _ => BitOperations.PopCount(Convert.ToUInt64(bitmask))
//        };
//        var builder = ImmutableArray.CreateBuilder<MavModeFlag>(count);

//        if (!EqualityComparer<TUnderlying>.Default.Equals((bitmask & (TUnderlying)Convert.ChangeType(MavModeFlag.SafetyArmed, typeof(TUnderlying))), default)) builder.Add(MavModeFlag.SafetyArmed);
//        if (!EqualityComparer<TUnderlying>.Default.Equals((bitmask & (TUnderlying)Convert.ChangeType(MavModeFlag.ManualInputEnabled, typeof(TUnderlying))), default)) builder.Add(MavModeFlag.ManualInputEnabled);
//        if (!EqualityComparer<TUnderlying>.Default.Equals((bitmask & (TUnderlying)Convert.ChangeType(MavModeFlag.HilEnabled, typeof(TUnderlying))), default)) builder.Add(MavModeFlag.HilEnabled);
//        if (!EqualityComparer<TUnderlying>.Default.Equals((bitmask & (TUnderlying)Convert.ChangeType(MavModeFlag.StabilizeEnabled, typeof(TUnderlying))), default)) builder.Add(MavModeFlag.StabilizeEnabled);
//        if (!EqualityComparer<TUnderlying>.Default.Equals((bitmask & (TUnderlying)Convert.ChangeType(MavModeFlag.GuidedEnabled, typeof(TUnderlying))), default)) builder.Add(MavModeFlag.GuidedEnabled);
//        if (!EqualityComparer<TUnderlying>.Default.Equals((bitmask & (TUnderlying)Convert.ChangeType(MavModeFlag.AutoEnabled, typeof(TUnderlying))), default)) builder.Add(MavModeFlag.AutoEnabled);
//        if (!EqualityComparer<TUnderlying>.Default.Equals((bitmask & (TUnderlying)Convert.ChangeType(MavModeFlag.TestEnabled, typeof(TUnderlying))), default)) builder.Add(MavModeFlag.TestEnabled);
//        if (!EqualityComparer<TUnderlying>.Default.Equals((bitmask & (TUnderlying)Convert.ChangeType(MavModeFlag.CustomModeEnabled, typeof(TUnderlying))), default)) builder.Add(MavModeFlag.CustomModeEnabled);

//        return builder.MoveToImmutable();
//#else
//		if (EqualityComparer<TUnderlying>.Default.Equals(bitmask, default))
//		{
//			return ImmutableArray<MavModeFlag>.Empty;
//		}
//		var builder = ImmutableArray.CreateBuilder<MavModeFlag>(8); // Max 8 flags in MavModeFlag

//		if (!EqualityComparer<TUnderlying>.Default.Equals((bitmask & (TUnderlying)Convert.ChangeType(MavModeFlag.SafetyArmed, typeof(TUnderlying))), default)) builder.Add(MavModeFlag.SafetyArmed);
//		if (!EqualityComparer<TUnderlying>.Default.Equals((bitmask & (TUnderlying)Convert.ChangeType(MavModeFlag.ManualInputEnabled, typeof(TUnderlying))), default)) builder.Add(MavModeFlag.ManualInputEnabled);
//		if (!EqualityComparer<TUnderlying>.Default.Equals((bitmask & (TUnderlying)Convert.ChangeType(MavModeFlag.HilEnabled, typeof(TUnderlying))), default)) builder.Add(MavModeFlag.HilEnabled);
//		if (!EqualityComparer<TUnderlying>.Default.Equals((bitmask & (TUnderlying)Convert.ChangeType(MavModeFlag.StabilizeEnabled, typeof(TUnderlying))), default)) builder.Add(MavModeFlag.StabilizeEnabled);
//		if (!EqualityComparer<TUnderlying>.Default.Equals((bitmask & (TUnderlying)Convert.ChangeType(MavModeFlag.GuidedEnabled, typeof(TUnderlying))), default)) builder.Add(MavModeFlag.GuidedEnabled);
//		if (!EqualityComparer<TUnderlying>.Default.Equals((bitmask & (TUnderlying)Convert.ChangeType(MavModeFlag.AutoEnabled, typeof(TUnderlying))), default)) builder.Add(MavModeFlag.AutoEnabled);
//		if (!EqualityComparer<TUnderlying>.Default.Equals((bitmask & (TUnderlying)Convert.ChangeType(MavModeFlag.TestEnabled, typeof(TUnderlying))), default)) builder.Add(MavModeFlag.TestEnabled);
//		if (!EqualityComparer<TUnderlying>.Default.Equals((bitmask & (TUnderlying)Convert.ChangeType(MavModeFlag.CustomModeEnabled, typeof(TUnderlying))), default)) builder.Add(MavModeFlag.CustomModeEnabled);

//		return builder.MoveToImmutable();
//#endif
//	}

//	/// <summary>
//	/// Determines whether the specified object is equal to the current instance by comparing their bitmask values.
//	/// </summary>
//	/// <param name="obj">The object to compare with the current instance.</param>
//	/// <returns><c>true</c> if the specified object is a <see cref="MavModeFlagBitmask{TUnderlying}"/> with the same bitmask; otherwise, <c>false</c>.</returns>
//	public override bool Equals(object? obj) => obj is MavModeFlagBitmask<TUnderlying> other && Equals(other);

//	/// <summary>
//	/// Determines whether the specified <see cref="MavModeFlagBitmask{TUnderlying}"/> instance is equal to the current instance by comparing their bitmask values.
//	/// </summary>
//	/// <param name="other">The <see cref="MavModeFlagBitmask{TUnderlying}"/> instance to compare with the current instance.</param>
//	/// <returns><c>true</c> if the bitmask values are equal; otherwise, <c>false</c>.</returns>
//	public bool Equals(MavModeFlagBitmask<TUnderlying> other) => EqualityComparer<TUnderlying>.Default.Equals(_bitmask, other._bitmask);

//	/// <summary>
//	/// Returns the hash code for the bitmask value.
//	/// </summary>
//	/// <returns>A hash code based on the underlying bitmask value.</returns>
//	public override int GetHashCode() => _bitmask.GetHashCode();

//	/// <summary>
//	/// Determines whether two <see cref="MavModeFlagBitmask{TUnderlying}"/> instances have the same bitmask value.
//	/// </summary>
//	/// <param name="left">The first instance to compare.</param>
//	/// <param name="right">The second instance to compare.</param>
//	/// <returns><c>true</c> if the bitmask values are equal; otherwise, <c>false</c>.</returns>
//	public static bool operator ==(MavModeFlagBitmask<TUnderlying> left, MavModeFlagBitmask<TUnderlying> right) => left.Equals(right);

//	/// <summary>
//	/// Determines whether two <see cref="MavModeFlagBitmask{TUnderlying}"/> instances have different bitmask values.
//	/// </summary>
//	/// <param name="left">The first instance to compare.</param>
//	/// <param name="right">The second instance to compare.</param>
//	/// <returns><c>true</c> if the bitmask values are different; otherwise, <c>false</c>.</returns>
//	public static bool operator !=(MavModeFlagBitmask<TUnderlying> left, MavModeFlagBitmask<TUnderlying> right) => !left.Equals(right);

//	/// <summary>
//	/// Returns a string representation of the bitmask and its active flags.
//	/// </summary>
//	/// <returns>A string in the format "Bitmask: [value], Active flags: [flag1, flag2, ...]".</returns>
//	public override string ToString() => $"Bitmask: {_bitmask}, Active flags: [{string.Join(", ", ActiveFlags)}]";
//}
