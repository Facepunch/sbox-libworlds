using System;
using Sandbox.Diagnostics;

namespace Sandbox.Worlds;

public enum CellState
{
	Unloaded,
	Loading,
	Ready
}

public delegate void WorldCellOpacityChanged( WorldCell cell, float opacity );

public sealed class WorldCell : Component, Component.ExecuteInEditor
{
	private float _opacity = 0f;

	[Property]
	public Vector3Int Index { get; set; }

	public CellState State { get; private set; }

	public float Opacity
	{
		get => _opacity;
		set
		{
			value = Math.Clamp( value, 0f, 1f );

			if ( _opacity == value ) return;

			_opacity = value;

			OpacityChanged?.Invoke( this, value );
		}
	}

	internal bool IsMaskedByParent { get; private set; }
	internal bool IsMaskedByChild { get; private set; }

	public event WorldCellOpacityChanged? OpacityChanged;

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
		State = CellState.Ready;
	}

	internal void Unload()
	{
		State = CellState.Unloaded;
	}

	protected override void OnUpdate()
	{
		IsMaskedByParent = World.AreParentCellsVisible( Index );
		IsMaskedByChild = World.IsChildCellVisible( Index );

		var targetOpacity = IsMaskedByParent || IsMaskedByChild || State == CellState.Loading ? 0f : GetDistanceOpacity();

		Opacity += Math.Sign( targetOpacity - Opacity ) * Time.Delta;

		if ( State == CellState.Unloaded && Opacity <= 0f )
		{
			GameObject.Destroy();
		}
	}

	private float GetDistanceOpacity()
	{
		return 1f;

		if ( World.Child is null ) return 1f;
		if ( (Scene.Camera?.Transform.World ?? World.EditorCameraTransform) is not { } camTransform ) return 1f;

		var cellSize = World.CellSize;
		var camPos = camTransform.Position;
		var center = Transform.Position + new Vector3( cellSize, cellSize, World.CellHeight ) * 0.5f;

		if ( World.Is2D )
		{
			camPos.z = 0f;
			center.z = 0f;
		}

		var dist = (camPos - center).Length / cellSize;

		return 1f - Math.Clamp( dist - World.LoadRadius + 1f, 0f, 1f );
	}
}
