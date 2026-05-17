using System;

namespace Runner.Events;

public readonly record struct RaceStarted( Guid RaceId, int PlayerCount );

public readonly record struct RaceCheckpointHit( Guid RaceId, ulong PlayerId, int CheckpointIndex, float ServerTime );

public readonly record struct RaceFinished( Guid RaceId, ulong WinnerId, float WinnerTimeSeconds );
