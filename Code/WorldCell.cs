using System;
using Sandbox.Diagnostics;

namespace Sandbox.Worlds;

/// <summary>
/// Describes the life cycle of a <see cref="WorldCell"/>.
/// </summary>
public enum CellState
{
	/// <summary>
	/// The cell has just been created, and hasn't loaded yet.
	/// </summary>
	Initializing,

	/// <summary>
	/// The cell is asynchronously loading, and is not yet visible.
	/// </summary>
	Loading,

	/// <summary>
	/// The cell is loaded, and can be visible if needed.
	/// </summary>
	Ready,

	/// <summary>
	/// The cell has been unloaded, and is no longer valid.
	/// </summary>
	Unloaded
}

/// <summary>
/// Delegate for when a <see cref="WorldCell"/> changes opacity. Cells fade in and out
/// depending on their distance from the camera.
/// </summary>
/// <param name="cell">Which cell has changed opacity.</param>
/// <param name="opacity">The new opacity, between <c>0</c> and <c>1</c>.</param>
public delegate void WorldCellOpacityChanged( WorldCell cell, float opacity );

/// <summary>
/// <para>
/// A cell within the grid of a <see cref="StreamingWorld"/>.
/// </para>
/// <para>
/// A <see cref="ICellLoader"/> can control what gets spawned in the cell, and can call
/// <see cref="MarkLoading"/> if processing needs to be performed asynchronously.
/// After loading is complete, <see cref="MarkReady"/> is called.
/// </para>
/// <para>
/// Loaded cells will fade in and out their <see cref="Opacity"/> based on distance from the camera.
/// </para>
/// </summary>
[Hide, Icon( "grid_3x3" )]
public sealed class WorldCell : Component, Component.ExecuteInEditor
{
	private float _opacity = 0f;

	public StreamingWorld World { get; private set; } = null!;

	/// <summary>
	/// This cell's coordinate in the grid of its detail level.
	/// </summary>
	public CellIndex Index { get; private set; }

	/// <summary>
	/// The detail level of this cell, with <c>0</c> being the most detailed.
	/// </summary>
	public int Level => Index.Level;

	/// <summary>
	/// The size of this cell in world space. For level <c>0</c> this will be
	/// <see cref="StreamingWorld.BaseCellSize"/>, and for each level above that it will double.
	/// </summary>
	public Vector3 Size { get; private set; }

	/// <summary>
	/// The status of this cell in its life cycle. Only <see cref="CellState.Ready"/> cells
	/// can be visible.
	/// </summary>
	public CellState State { get; private set; }

	/// <summary>
	/// The current opacity of this cell, between <c>0</c> and <c>1</c>. Cells will fade in and
	/// out of visibility based on the distance from the camera.
	/// </summary>
	public float Opacity
	{
		get => _opacity;
		private set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( _opacity == value ) return;

			_opacity = value;

			OpacityChanged?.Invoke( this, value );
		}
	}

	/// <summary>
	/// Event dispatched when this cell changes <see cref="Opacity"/>.
	/// </summary>
	public event WorldCellOpacityChanged? OpacityChanged;

	internal bool IsChildOutOfRange { get; set; }
	internal bool IsMaskedByChild { get; set; }
	internal bool IsOutOfRange { get; set; }

	internal void Initialize( StreamingWorld world, CellIndex index )
	{
		World = world;
		Index = index;
		Size = world.GetCellSize( index.Level );
	}

	/// <summary>
	/// Called within an implementation of <see cref="ICellLoader.LoadCell"/> to declare that this
	/// cell is loading asynchronously, and can't be visible yet.
	/// </summary>
	public void MarkLoading()
	{
		ThreadSafe.AssertIsMainThread();
		Assert.True( State == CellState.Initializing, $"Can only call during {nameof(ICellLoader)}.{nameof(ICellLoader.LoadCell)}." );

		State = CellState.Loading;
	}

	/// <summary>
	/// Called after <see cref="MarkLoading"/> when this cell is ready to be displayed.
	/// </summary>
	public void MarkReady()
	{
		Assert.False( State == CellState.Unloaded, "Can't mark unloaded cells as ready." );

		State = CellState.Ready;
	}

	internal void Unload()
	{
		State = CellState.Unloaded;
	}

	internal void UpdateOpacity()
	{
		var targetOpacity = GetTargetOpacity();
		Opacity += Math.Sign( targetOpacity - Opacity ) * Time.Delta * 0.5f;
	}

	private float GetTargetOpacity()
	{
		if ( State != CellState.Ready )
		{
			return 0f;
		}

		if ( IsChildOutOfRange )
		{
			return 1f;
		}

		if ( IsOutOfRange && World.CanFadeOutCell( Index ) || IsMaskedByChild )
		{
			return 0f;
		}

		return GetDistanceOpacity();
	}

	private float GetDistanceOpacity()
	{
		return 1f;
	}
}
