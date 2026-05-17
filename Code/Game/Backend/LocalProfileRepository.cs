using System;
using System.Threading;
using System.Threading.Tasks;
using Sandbox;
using Runner.Player;

namespace Runner.Backend;

/// <summary>
/// Persists <see cref="PlayerProfile"/> as JSON in S&amp;box's per-user <c>FileSystem.Data</c>.
/// One file per player id: <c>profiles/{id}.json</c>.
/// Uses S&amp;box's whitelisted JSON writer (FileSystem.Data.WriteJson / ReadJson) — the bare
/// System.Text.Json reflection path NRE'd inside the sandbox.
/// Swap with a cloud-backed implementation later — callers depend on <see cref="IProfileRepository"/> only.
/// </summary>
public sealed class LocalProfileRepository : IProfileRepository
{
	private const string DirName = "profiles";

	private static string FileNameFor( ulong playerId )
		=> $"{DirName}/{(playerId == 0UL ? "local" : playerId.ToString())}.json";

	public Task<PlayerProfile> LoadAsync( ulong playerId, CancellationToken ct = default )
	{
		try
		{
			var path = FileNameFor( playerId );
			if ( !FileSystem.Data.FileExists( path ) )
				return Task.FromResult<PlayerProfile>( null );

			var profile = FileSystem.Data.ReadJson<PlayerProfile>( path );
			return Task.FromResult( profile );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"[LocalProfileRepository] Load failed for {playerId}: {ex.Message}" );
			return Task.FromResult<PlayerProfile>( null );
		}
	}

	public Task SaveAsync( PlayerProfile profile, CancellationToken ct = default )
	{
		if ( profile is null )
			return Task.CompletedTask;

		try
		{
			if ( !FileSystem.Data.DirectoryExists( DirName ) )
				FileSystem.Data.CreateDirectory( DirName );

			profile.LastSeenAt = DateTime.UtcNow;
			FileSystem.Data.WriteJson( FileNameFor( profile.PlayerId ), profile );
		}
		catch ( Exception ex )
		{
			Log.Error( $"[LocalProfileRepository] Save failed for {profile.PlayerId}: {ex.Message}\n{ex.StackTrace}" );
		}
		return Task.CompletedTask;
	}

	public Task<bool> DeleteAsync( ulong playerId, CancellationToken ct = default )
	{
		try
		{
			var path = FileNameFor( playerId );
			if ( FileSystem.Data.FileExists( path ) )
			{
				FileSystem.Data.DeleteFile( path );
				return Task.FromResult( true );
			}
		}
		catch ( Exception ex )
		{
			Log.Warning( $"[LocalProfileRepository] Delete failed for {playerId}: {ex.Message}" );
		}
		return Task.FromResult( false );
	}
}
