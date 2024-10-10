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

[Hide]
public sealed class WorldCell : Component, Component.ExecuteInEditor
{
	private float _opacity = 0f;

	public CellIndex Index { get; set; }

	public int Level => Index.Level;

	public Vector3 Size { get; set; }

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

	internal bool IsChildOutOfRange { get; set; }
	internal bool IsMaskedByChild { get; set; }
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
