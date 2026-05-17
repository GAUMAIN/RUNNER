using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sandbox;
using Runner.Player;

namespace Runner.Backend;

/// <summary>
/// Persists <see cref="PlayerProfile"/> as JSON in S&amp;box's per-user <c>FileSystem.Data</c>.
/// One file per player id: <c>profiles/{id}.json</c>.
/// Swap with a cloud-backed implementation later — callers depend on <see cref="IProfileRepository"/> only.
/// </summary>
public sealed class LocalProfileRepository : IProfileRepository
{
	private const string DirName = "profiles";

	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true,
	};

	private static string FileNameFor( ulong playerId )
		=> $"{DirName}/{(playerId == 0UL ? "local" : playerId.ToString())}.json";

	public Task<PlayerProfile> LoadAsync( ulong playerId, CancellationToken ct = default )
	{
		try
		{
			var path = FileNameFor( playerId );
			if ( !FileSystem.Data.FileExists( path ) )
				return Task.FromResult<PlayerProfile>( null );

			var json = FileSystem.Data.ReadAllText( path );
			var profile = JsonSerializer.Deserialize<PlayerProfile>( json, JsonOpts );
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
			var json = JsonSerializer.Serialize( profile, JsonOpts );
			FileSystem.Data.WriteAllText( FileNameFor( profile.PlayerId ), json );
		}
		catch ( Exception ex )
		{
			Log.Error( $"[LocalProfileRepository] Save failed for {profile.PlayerId}: {ex.Message}" );
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
