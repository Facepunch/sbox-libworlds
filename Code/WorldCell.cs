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

	internal bool IsParentOutOfRange { get; set; }
	internal bool IsMaskedByParent { get; set; }
	internal bool IsOutOfRange { get; set; }

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

		if ( IsParentOutOfRange )
		{
			return 1f;
		}

		if ( IsOutOfRange && World.CanFadeOutCell( Index ) || IsMaskedByParent )
		{
			return 0f;
		}

		return GetDistanceOpacity();
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
