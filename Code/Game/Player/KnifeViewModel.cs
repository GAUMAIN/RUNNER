using System;
using System.Linq;
using Sandbox;
using Runner.Economy;

namespace Runner.Player;

/// <summary>
/// Renders the equipped knife model attached to the citizen body's right-hand
/// grip bone, for BOTH first-person and third-person.
///
/// Since we pivoted to "citizen body visible in FPS" (with the head bone
/// collapsed to hide the skull), there's no longer a separate FPS arms model
/// to attach the knife to. The citizen rig drives all animations
/// (citizen@melee_weapons_2h_*), so the hold_R bone naturally moves with
/// idle, run, and attack animations.
///
/// We sample hold_R's world transform each frame in OnPreRender (after the
/// animgraph has run) and write it to a free-in-the-scene GameObject that
/// holds a single ModelRenderer with the katana .vmdl.
/// </summary>
public sealed class KnifeViewModel : Component
{
	// ─── Bone attach ──────────────────────────────────────────────────────

	[Property] public string HoldBoneName { get; set; } = "hold_R";

	/// <summary>Local-space tweak applied on top of the bone's world transform.</summary>
	[Property] public Vector3 KnifeLocalOffset   { get; set; } = new Vector3( 0, 0, 0 );
	[Property] public Angles  KnifeLocalRotation { get; set; } = new Angles( 0, 0, 0 );
	[Property] public float   KnifeUniformScale  { get; set; } = 0.5f;

	// ─── Render flags ─────────────────────────────────────────────────────

	/// <summary>
	/// When true the knife renders as a viewmodel overlay (draws on top, no
	/// world clipping). Defaults to false — the citizen body's hand will hide
	/// it naturally if the camera goes behind, which is fine for a melee weapon.
	/// </summary>
	[Property] public bool UseOverlayRender { get; set; } = false;

	// ─── Inspect (legacy hook, sound-only for now) ────────────────────────

	[Property] public SoundEvent InspectSound { get; set; }
	[Property] public float InspectCooldown { get; set; } = 1.0f;

	private TimeSince _timeSinceInspect = 999f;

	// ─── Runtime state ────────────────────────────────────────────────────

	private GameObject _knifeRoot;
	private ModelRenderer _knifeRenderer;
	private string _knifeLoadedId;

	private GameObject _resolvedHandBone;
	private bool _boneWarned;

	protected override void OnEnabled()
	{
		base.OnEnabled();
		BuildMesh();
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();
		if ( _knifeRoot.IsValid() )
		{
			_knifeRoot.Destroy();
			_knifeRoot = null;
		}
		_resolvedHandBone = null;
	}

	private void BuildMesh()
	{
		if ( _knifeRoot.IsValid() ) return;

		_knifeRoot = new GameObject( true, "Knife_Held" );
		_knifeRenderer = _knifeRoot.Components.Create<ModelRenderer>();
		_knifeRenderer.RenderOptions.Overlay = UseOverlayRender;
		_knifeRenderer.RenderOptions.Game = !UseOverlayRender;
		_knifeRenderer.RenderType = ModelRenderer.ShadowRenderType.Off;
	}

	// ─── Per-frame ────────────────────────────────────────────────────────

	protected override void OnUpdate()
	{
		// Inspect input (sound only — the animgraph drives the visual).
		if ( !IsProxy
			&& !Runner.Config.UserSettings.IsPaused
			&& _timeSinceInspect > InspectCooldown
			&& Input.Pressed( "Inspect" ) )
		{
			_timeSinceInspect = 0f;
			PlayInspectSound();
		}
	}

	protected override void OnPreRender()
	{
		if ( !_knifeRoot.IsValid() )
			BuildMesh();
		if ( !_knifeRoot.IsValid() ) return;

		var pawn = GameObject.Components.Get<PlayerPawn>();
		var stats = GameObject.Components.Get<PlayerStats>();

		if ( !pawn.IsValid() || !stats.IsValid() )
		{
			_knifeRoot.Enabled = false;
			return;
		}

		var knife = string.IsNullOrEmpty( stats.EquippedKnifeId )
			? null
			: KnifeCatalog.GetById( stats.EquippedKnifeId );
		if ( knife is null )
		{
			_knifeRoot.Enabled = false;
			return;
		}

		var handGo = ResolveHandBone( pawn );
		if ( !handGo.IsValid() )
		{
			_knifeRoot.Enabled = false;
			return;
		}

		LoadKnifeModelIfNeeded( knife.Id );
		if ( !_knifeRoot.IsValid() || _knifeRenderer.Model is null )
		{
			_knifeRoot.Enabled = false;
			return;
		}

		_knifeRoot.Enabled = true;

		var localRot = KnifeLocalRotation.ToRotation();
		_knifeRoot.WorldRotation = handGo.WorldRotation * localRot;
		_knifeRoot.WorldPosition = handGo.WorldPosition + handGo.WorldRotation * KnifeLocalOffset;
		_knifeRoot.WorldScale = Vector3.One * KnifeUniformScale;

		_knifeRenderer.Tint = TintFor( knife.Rarity );
	}

	// ─── Model loading (mirrors FpsViewmodel logic) ───────────────────────

	private static string[] ModelPathsFor( string knifeId )
	{
		// katana/katana.vmdl is the only knife model fully mounted on this
		// install (verified — has both .vmdl and its .vmat + textures).
		// box.vmdl always loads as a last-resort placeholder.
		return new[] { "katana/katana.vmdl", "models/dev/box.vmdl" };
	}

	private void LoadKnifeModelIfNeeded( string knifeId )
	{
		if ( _knifeLoadedId == knifeId ) return;

		Model resolved = null;
		string resolvedPath = null;

		foreach ( var path in ModelPathsFor( knifeId ) )
		{
			var m = Model.Load( path );
			if ( m is null ) continue;
			if ( m.Bounds.Size.IsNearlyZero() ) continue;

			var loaded = m.ResourcePath?.Replace( '\\', '/' ).ToLowerInvariant() ?? "";
			var req = path.Replace( '\\', '/' ).ToLowerInvariant();
			if ( !loaded.EndsWith( req ) && !req.EndsWith( loaded ) ) continue;

			resolved = m;
			resolvedPath = path;
			break;
		}

		if ( resolved is null )
		{
			Log.Warning( $"[KnifeViewModel] No knife model resolved for '{knifeId}'." );
			return;
		}

		_knifeRenderer.Model = resolved;
		_knifeLoadedId = knifeId;
		Log.Info( $"[KnifeViewModel] Loaded '{resolvedPath}' for '{knifeId}'" );
	}

	// ─── Hand bone resolution ─────────────────────────────────────────────

	private GameObject ResolveHandBone( PlayerPawn pawn )
	{
		if ( _resolvedHandBone.IsValid() )
			return _resolvedHandBone;

		var body = pawn.BodyRenderer;
		if ( !body.IsValid() || body.Model is null ) return null;

		string[] candidates = { HoldBoneName, "hold_R", "hand_R" };

		int n = body.Model.BoneCount;
		foreach ( var cand in candidates )
		{
			if ( string.IsNullOrEmpty( cand ) ) continue;
			for ( int i = 0; i < n; i++ )
			{
				var o = body.GetBoneObject( i );
				if ( !o.IsValid() ) continue;
				if ( o.Name != cand ) continue;

				_resolvedHandBone = o;
				Log.Info( $"[KnifeViewModel] Resolved hand bone '{cand}' at index {i}" );
				return o;
			}
		}

		if ( !_boneWarned )
		{
			_boneWarned = true;
			Log.Warning( $"[KnifeViewModel] No hand bone found on body rig (BoneCount={n}). Knife won't render." );
		}
		return null;
	}

	// ─── Sound ────────────────────────────────────────────────────────────

	private void PlayInspectSound()
	{
		var handle = InspectSound is not null
			? Sound.Play( InspectSound )
			: Sound.Play( "sounds/kenney/ui/ui.button.press.sound" );
		if ( handle is not null )
			handle.Volume *= Runner.Config.UserSettings.Volume;
	}

	// ─── Tint by rarity ───────────────────────────────────────────────────

	private static Color TintFor( KnifeRarity r ) => r switch
	{
		KnifeRarity.Common   => Color.White,
		KnifeRarity.Uncommon => new Color( 0.78f, 1.00f, 0.85f ),
		KnifeRarity.Rare     => new Color( 0.78f, 0.88f, 1.00f ),
		KnifeRarity.Epic     => new Color( 0.95f, 0.78f, 1.00f ),
		KnifeRarity.Mythic   => new Color( 1.00f, 0.80f, 0.65f ),
		KnifeRarity.Secret   => new Color( 1.00f, 0.92f, 0.65f ),
		_ => Color.White
	};
}
