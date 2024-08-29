using System;
using Sandbox.Diagnostics;

namespace Sandbox.Worlds;

public enum CellState
{
	Unloaded,
	Loading,
	Ready
}

public delegate void WorldCellHideStateChanged( WorldCell cell, bool hidden );

public sealed class WorldCell : Component
{
	private bool _isHidden;

	[Property]
	public Vector3Int Index { get; set; }

	public CellState State { get; private set; }

	public bool IsHidden
	{
		get => _isHidden;
		set
		{
			if ( _isHidden == value ) return;

			_isHidden = value;

			HideStateChanged?.Invoke( this, value );
		}
	}

	public event WorldCellHideStateChanged? HideStateChanged;

	private StreamingWorld? _world;

	public StreamingWorld World => _world ??=
		Components.Get<StreamingWorld>( FindMode.Enabled | FindMode.Disabled | FindMode.InParent )
			?? throw new Exception( $"{nameof(WorldCell)} must be a child object of a {nameof(StreamingWorld)}." );

	public void MarkLoading()
	{
		ThreadSafe.AssertIsMainThread();
		Assert.True( State == CellState.Unloaded, $"Can only call during {nameof(ICellLoader)}.{nameof(ICellLoader.LoadCell)}." );

		State = CellState.Loading;
	}

	public void MarkReady()
	{
		ThreadSafe.AssertIsMainThread();

		if ( State == CellState.Ready ) return;

		State = CellState.Ready;

		IsHidden = World.ShouldHideCell( Index );
		World.DispatchCellReady( Index );
	}
}
