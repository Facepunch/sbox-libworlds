using System;
using System.Collections.Generic;

namespace Sandbox.Worlds;

public sealed class StreamingWorld : Component, Component.ExecuteInEditor
{
	private readonly Dictionary<Vector3Int, WorldCell> _cells = new();

	private readonly HashSet<Vector3Int> _loadOrigins = new();
	private readonly HashSet<Vector3Int> _cellsToLoad = new();
	private readonly HashSet<Vector3Int> _cellsToUnload = new();

	private Transform? _editorCameraTransform;

	[Property]
	public bool Is2D { get; set; }

	[Property] public float CellSize { get; set; } = 1024f;

	[Property, HideIf( nameof(Is2D), true )]
	public float CellHeight { get; set; } = 1024f;

	/// <summary>
	/// How many cells away from a <see cref="LoadOrigin"/> should be loaded.
	/// </summary>
	[Property] public int LoadRadius { get; set; } = 4;

	protected override void OnUpdate()
	{
		_loadOrigins.Clear();

		_cellsToLoad.Clear();
		_cellsToUnload.Clear();

		foreach ( var origin in Scene.GetAllComponents<LoadOrigin>() )
		{
			_loadOrigins.Add( GetCellIndex( origin.Transform.Position ) );
		}

		if ( _editorCameraTransform is { Position: { } editorCamPos } )
		{
			_loadOrigins.Add( GetCellIndex( editorCamPos ) );
		}

		foreach ( var loadOrigin in _loadOrigins )
		{
			LoadCellsAround( loadOrigin );
		}

		var unloadRadius = LoadRadius + 1;

		foreach ( var cellIndex in _cells.Keys )
		{
			if ( !_cellsToLoad.Remove( cellIndex ) )
			{
				var inRange = false;

				foreach ( var loadOrigin in _loadOrigins )
				{
					if ( (loadOrigin - cellIndex).LengthSquared <= unloadRadius * unloadRadius )
					{
						inRange = true;
						break;
					}
				}

				if ( !inRange )
				{
					_cellsToUnload.Add( cellIndex );
				}
			}
		}

		foreach ( var cellIndex in _cellsToUnload )
		{
			UnloadCell( cellIndex );
		}


		foreach ( var cellIndex in _cellsToLoad )
		{
			LoadCell( cellIndex );
		}
	}

	public Vector3Int GetCellIndex( Vector3 worldPosition )
	{
		var localPos = Transform.World.PointToLocal( worldPosition );
		return new Vector3Int(
			(int)MathF.Floor( localPos.x / CellSize ),
			(int)MathF.Floor( localPos.y / CellSize ),
			Is2D ? 0 : ( int)MathF.Floor( localPos.z / CellHeight ) );
	}

	private void LoadCellsAround( Vector3Int cellIndex )
	{
		if ( !Is2D ) throw new NotImplementedException();

		for ( var dx = -LoadRadius; dx <= LoadRadius; ++dx )
		{
			var maxDy = (int)MathF.Sqrt( LoadRadius * LoadRadius - dx * dx );
			var minDy = -maxDy;

			for ( var dy = minDy; dy <= maxDy; ++dy )
			{
				_cellsToLoad.Add( new Vector3Int( cellIndex.x + dx, cellIndex.y + dy, 0 ) );
			}
		}
	}

	private void LoadCell( Vector3Int cellIndex )
	{
		var go = new GameObject( false, Is2D
			? $"Cell {cellIndex.x} {cellIndex.y}"
			: $"Cell {cellIndex.x} {cellIndex.y} {cellIndex.z}" )
		{
			Transform = { Position = new Vector3( cellIndex.x * CellSize, cellIndex.y * CellSize, cellIndex.z * CellHeight ) },
			Parent = GameObject,
			Flags = GameObjectFlags.NotSaved
		};

		var cell = go.Components.Create<WorldCell>();

		cell.Index = cellIndex;

		_cells.Add( cellIndex, cell );

		cell.GameObject.Enabled = true;

		Components.Get<ICellLoader>()?.LoadCell( cell );
	}

	private void UnloadCell( Vector3Int cellIndex )
	{
		if ( !_cells.Remove( cellIndex, out var cell ) ) return;

		Components.Get<ICellLoader>()?.UnloadCell( cell );

		cell.GameObject.Destroy();
	}

	protected override void DrawGizmos()
	{
		_editorCameraTransform = Gizmo.CameraTransform;
	}
}
