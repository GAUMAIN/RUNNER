using System;

namespace Runner.Events;

public readonly record struct PlayerLeveledUp( ulong PlayerId, int NewLevel, int OldLevel );

public readonly record struct PlayerXpGranted( ulong PlayerId, long Amount, string Reason );

public readonly record struct PlayerSpeedChanged( ulong PlayerId, float NewSpeed );

public readonly record struct PlayerRebirthed( ulong PlayerId, int RebirthCount );

public readonly record struct PlayerCurrencyChanged( ulong PlayerId, string Currency, long Delta, long NewBalance );
