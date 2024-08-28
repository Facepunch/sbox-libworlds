using System;
using Sandbox.Diagnostics;

namespace Sandbox.Worlds;

public enum CellState
{
	Unloaded,
	Loading,
	Ready
}

public sealed class WorldCell : Component
{
	[Property]
	public Vector3Int Index { get; set; }

	public CellState State { get; private set; }

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

		World.DispatchCellReady( Index );
	}
}
