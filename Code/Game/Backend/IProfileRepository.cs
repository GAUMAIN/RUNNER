using System.Threading;
using System.Threading.Tasks;
using Runner.Player;

namespace Runner.Backend;

/// <summary>
/// Persistence abstraction. Swap implementations: local file (dev), cloud (prod).
/// Always async; never block a game tick on I/O.
/// </summary>
public interface IProfileRepository
{
	Task<PlayerProfile?> LoadAsync( ulong playerId, CancellationToken ct = default );
	Task SaveAsync( PlayerProfile profile, CancellationToken ct = default );
	Task<bool> DeleteAsync( ulong playerId, CancellationToken ct = default );
}
