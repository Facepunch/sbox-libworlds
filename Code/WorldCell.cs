using System;

namespace Sandbox.Worlds;

public sealed class WorldCell : Component
{
	[Property]
	public Vector3Int Index { get; set; }

	private StreamingWorld? _world;

	public StreamingWorld World => _world ??=
		Components.Get<StreamingWorld>( FindMode.Enabled | FindMode.Disabled | FindMode.InParent )
			?? throw new Exception( $"{nameof(WorldCell)} must be a child object of a {nameof(StreamingWorld)}." );
}
