using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json.Serialization;
using Sandbox.Diagnostics;

namespace Sandbox.Worlds;

/// <summary>
/// Identifies a cell in a <see cref="StreamingWorld"/>.
/// </summary>
/// <param name="Position">
/// Position index of the cell in its detail level.
/// Neighbouring cells will differ by <c>±1</c> in one axis.
/// For 2D worlds, the <c>z</c> component will always be <c>0</c>.
/// </param>
/// <param name="Level">Detail level of the cell. Level <c>0</c> is the highest.</param>
public record struct CellIndex( int Level, Vector3Int Position )
{
	/// <summary>
	/// The index of the cell, one level of detail lower than this one, that contains this cell.
	/// </summary>
	[JsonIgnore]
	public CellIndex Parent => new( Level + 1,
		new Vector3Int(
			Position.x < 0 ? Position.x - 1 : Position.x,
			Position.y < 0 ? Position.y - 1 : Position.y,
			Position.z < 0 ? Position.z - 1 : Position.z ) / 2 );

	/// <summary>
	/// The index of the cell, one level of detail higher than this one, with the minimum position still contained by this cell.
	/// In 2D worlds there will be 4 child cells per lower-detail cell, and in 3D there will be 8.
	/// </summary>
	[JsonIgnore]
	public CellIndex FirstChild => new( Level - 1, Position * 2 );

	public override string ToString()
	{
		return $"{{ Level = {Level}, Position = {Position} }}";
	}
}

/// <summary>
/// A world made out of a 2D or 3D grid of cells, optionally with several levels of detail. Cells are loaded on demand,
/// deferring to one or more <see cref="ICellLoader"/> components to handle loading / unloading.
/// </summary>
[Icon( "public" )]
public sealed class StreamingWorld : Component, Component.ExecuteInEditor
{
	private record struct Level( int Index, Dictionary<Vector3Int, WorldCell> Cells, HashSet<Vector3Int> LoadOrigins );

	private readonly List<Level> _levels = new();

	private readonly HashSet<CellIndex> _cellsToLoad = new();
	private readonly HashSet<CellIndex> _cellsToUnload = new();

	private int _levelCount = 1;
	private bool _is2d = true;
	private int _loadRadius = 4;

	/// <summary>
	/// How many levels of detail does this world use. Minimum is 1. Each level is half
	/// the resolution of the previous one, and loads in at twice the distance.
	/// </summary>
	[Property]
	public int DetailLevels
	{
		get
		{
			return _levelCount;
		}

		set
		{
			value = Math.Max( 1, value );

			if ( value == _levelCount ) return;

			_levelCount = value;
			UpdateLevelCount();
		}
	}

	/// <summary>
	/// If true, use a 2D grid of cells instead of 3D.
	/// </summary>
	[Property]
	public bool Is2D
	{
		get => _is2d;
		set
		{
			if ( _is2d == value ) return;

			_is2d = value;
			UpdateLoadRadius();
		}
	}

	/// <summary>
	/// Size of one cell in each axis at the highest level of detail.
	/// </summary>
	[Property] public float BaseCellSize { get; set; } = 1024f;

	/// <summary>
	/// How many cells away from a <see cref="LoadOrigin"/> or active camera should be loaded, in each detail level.
	/// </summary>
	[Property, Range( 1, 16, 1 )]
	public int LoadRadius
	{
		get => _loadRadius;
		set
		{
			value = Math.Clamp( value, 1, 16 );

			if ( _loadRadius == value ) return;

			_loadRadius = value;
			UpdateLoadRadius();
		}
	}

	internal Transform? EditorCameraTransform { get; private set; }

	public StreamingWorld()
	{
		UpdateLoadRadius();
		UpdateLevelCount();
	}

	/// <summary>
	/// Immediately remove all cells in the world.
	/// </summary>
	public void Clear()
	{
		foreach ( var level in _levels )
		{
			foreach ( var cell in level.Cells.Values.ToArray() )
			{
				UnloadCell( cell.Index );
			}

			Assert.AreEqual( 0, level.Cells.Count );
		}
	}

	public bool TryGetCell( CellIndex index, [NotNullWhen( true )] out WorldCell? cell )
	{
		cell = null;

		if ( index.Level < 0 || index.Level >= DetailLevels ) return false;

		return _levels[index.Level].Cells.TryGetValue( index.Position, out cell );
	}

	protected override void OnUpdate()
	{
		UpdateCells();
	}

	private void FindLoadOrigins()
	{
		if ( !Game.IsEditor || Game.IsPlaying || Game.IsPaused )
		{
			foreach ( var origin in Scene.GetAllComponents<LoadOrigin>() )
			{
				if ( origin.MaxLevel is { } maxLevel )
				{
					AddLoadOrigin( origin.WorldPosition, maxLevel );
				}
				else
				{
					AddLoadOrigin( origin.WorldPosition );
				}
			}

			foreach ( var camera in Scene.GetAllComponents<CameraComponent>() )
			{
				AddLoadOrigin( camera.WorldPosition );
			}
		}

		if ( EditorCameraTransform is { Position: var editorCamPos } )
		{
			AddLoadOrigin( editorCamPos );
		}
	}

	private void AddLoadOrigin( Vector3 position )
	{
		AddLoadOrigin( position, DetailLevels );
	}

	private void AddLoadOrigin( Vector3 position, int maxLevel )
	{
		for ( var i = 0; i < maxLevel && i < DetailLevels; ++i )
		{
			_levels[i].LoadOrigins.Add( GetCellIndex( position, i ).Position );
		}
	}

	public Vector3 GetCellSize( int level )
	{
		var size = BaseCellSize * (1 << level);

		return Is2D ? new Vector3( size, size, 0f ) : size;
	}

	private CellIndex GetCellIndex( Vector3 worldPosition, int level )
	{
		var cellSize = GetCellSize( level );
		var localPos = Transform.World.PointToLocal( worldPosition );

		return new CellIndex( level,
			new Vector3Int(
				(int)MathF.Floor( localPos.x / cellSize.x ),
				(int)MathF.Floor( localPos.y / cellSize.y ),
				Is2D ? 0 : (int)MathF.Floor( localPos.z / cellSize.z ) ) );
	}

	private Vector3Int[] _loadKernel = null!;

	private void UpdateLevelCount()
	{
		while ( _levels.Count > _levelCount )
		{
			var topLevel = _levels[^1];

			foreach ( var cell in topLevel.Cells.Values.ToArray() )
			{
				UnloadCell( cell.Index );
			}

			Assert.AreEqual( 0, topLevel.Cells.Count );

			_levels.RemoveAt( _levels.Count - 1 );
		}

		while ( _levels.Count < _levelCount )
		{
			_levels.Add( new Level( _levels.Count, new Dictionary<Vector3Int, WorldCell>(), new HashSet<Vector3Int>() ) );
		}
	}

	private void UpdateLoadRadius()
	{
		var list = new List<Vector3Int>();

		for ( var dx = -LoadRadius; dx <= LoadRadius; ++dx )
		{
			var maxDy = (int)MathF.Sqrt( LoadRadius * LoadRadius - dx * dx );
			var minDy = -maxDy;

			for ( var dy = minDy; dy <= maxDy; ++dy )
			{
				if ( Is2D )
				{
					list.Add( new Vector3Int( dx, dy, 0 ) );
					continue;
				}

				var maxDz = (int)MathF.Sqrt( LoadRadius * LoadRadius - dx * dx - dy * dy );
				var minDz = -maxDz;

				for ( var dz = minDz; dz <= maxDz; ++dz )
				{
					list.Add( new Vector3Int( dx, dy, dz ) );
				}
			}
		}

		_loadKernel = list.OrderBy( x => x.LengthSquared ).ToArray();
	}

	private void LoadCellsAround( CellIndex index )
	{
		foreach ( var delta in _loadKernel )
		{
			_cellsToLoad.Add( index with { Position = index.Position + delta } );
		}
	}

	internal (bool AllVisible, bool AnyOutOfRange) GetChildState( CellIndex cellIndex )
	{
		if ( cellIndex.Level <= 0 )
		{
			return (false, false);
		}

		var baseChildIndex = cellIndex.FirstChild;
		var allVisible = true;
		var anyOutOfRange = false;

		for ( var z = 0; z < (Is2D ? 1 : 2); z++ )
		for ( var y = 0; y < 2; y++ )
		for ( var x = 0; x < 2; x++ )
		{
			var childIndex = baseChildIndex with { Position = baseChildIndex.Position + new Vector3Int( x, y, z ) };

			if ( !TryGetCell( childIndex, out var cell ) )
			{
				allVisible = false;
				break;
			}

			anyOutOfRange |= cell.IsOutOfRange;
			allVisible &= cell.Opacity >= 1f || cell is { IsMaskedByChild: true };
		}

		return (allVisible, anyOutOfRange);
	}

	internal bool CanFadeOutCell( CellIndex cellIndex )
	{
		if ( !TryGetCell( cellIndex.Parent, out var parent ) )
		{
			// Parent doesn't exist! No hope of it fading in.
			return true;
		}

		return parent.Opacity >= 1f || parent.IsOutOfRange;
	}

	private void LoadCell( CellIndex cellIndex )
	{
		if ( cellIndex.Level < 0 || cellIndex.Level >= DetailLevels ) return;

		var size = GetCellSize( cellIndex.Level );

		var go = new GameObject( false, Is2D
			? $"Cell {cellIndex.Level} - {cellIndex.Position.x} {cellIndex.Position.y}"
			: $"Cell {cellIndex.Level} - {cellIndex.Position.x} {cellIndex.Position.y} {cellIndex.Position.z}" )
		{
			WorldPosition = cellIndex.Position * size,
			Parent = GameObject,
			Flags = GameObjectFlags.NotSaved | GameObjectFlags.Hidden,
			NetworkMode = NetworkMode.Never
		};

		var cell = go.Components.Create<WorldCell>();

		cell.Index = cellIndex;
		cell.Size = size;
		cell.Opacity = 0f;

		_levels[cellIndex.Level].Cells.Add( cellIndex.Position, cell );

		go.Enabled = true;

		foreach ( var cellLoader in Scene.GetAllComponents<ICellLoader>() )
		{
			try
			{
				cellLoader.LoadCell( cell );
			}
			catch ( Exception ex )
			{
				Log.Error( ex );
			}
		}

		if ( cell.State != CellState.Loading )
		{
			cell.MarkReady();
		}
	}

	private void UnloadCell( CellIndex cellIndex )
	{
		if ( cellIndex.Level < 0 || cellIndex.Level >= _levels.Count ) return;
		if ( !_levels[cellIndex.Level].Cells.Remove( cellIndex.Position, out var cell ) ) return;

		foreach ( var cellLoader in Scene.GetAllComponents<ICellLoader>().Reverse() )
		{
			try
			{
				cellLoader.UnloadCell( cell );
			}
			catch ( Exception ex )
			{
				Log.Error( ex );
			}
		}

		cell.Unload();
		cell.GameObject.Destroy();
	}

	protected override void OnDestroy()
	{
		Clear();
	}

	protected override void DrawGizmos()
	{
		EditorCameraTransform = Gizmo.CameraTransform;

		if ( !Gizmo.IsSelected ) return;

		Gizmo.Draw.IgnoreDepth = true;

		var levelCount = DetailLevels;

		foreach ( var level in _levels )
		{
			var cellSize = GetCellSize( level.Index );
			var margin = (levelCount - level.Index) * cellSize.x / (16 << level.Index);
			var color = new ColorHsv( level.Index * 30f, 1f, 1f );

			foreach ( var cell in level.Cells.Values )
			{
				Gizmo.Transform = cell.Transform.World;
				Gizmo.Draw.Color = color.WithAlpha( cell.Opacity * 0.25f );

				var bbox = new BBox(
					new Vector3( 0f, 0f, margin ),
					new Vector3( cellSize.x, cellSize.y, Is2D ? margin : cellSize.z ) );

				Gizmo.Draw.LineBBox( bbox );

				if ( cell.State == CellState.Loading || cell.IsMaskedByChild || cell.IsOutOfRange )
				{
					foreach ( var corner in bbox.Corners )
					{
						Gizmo.Draw.Line( corner, bbox.Center );
					}
				}
			}
		}
	}

	private void UpdateCells()
	{
		foreach ( var level in _levels )
		{
			level.LoadOrigins.Clear();
		}

		_cellsToLoad.Clear();
		_cellsToUnload.Clear();

		FindLoadOrigins();

		var unloadRadius = LoadRadius + 1;

		foreach ( var level in _levels )
		{
			foreach ( var loadOrigin in level.LoadOrigins )
			{
				LoadCellsAround( new CellIndex( level.Index, loadOrigin ) );
			}

			foreach ( var cell in level.Cells.Values )
			{
				if ( _cellsToLoad.Remove( cell.Index ) )
				{
					continue;
				}

				var inRange = false;

				foreach ( var loadOrigin in level.LoadOrigins )
				{
					if ( (loadOrigin - cell.Index.Position).LengthSquared <= unloadRadius * unloadRadius )
					{
						inRange = true;
						break;
					}
				}

				cell.IsOutOfRange = !inRange;

				if ( !inRange && cell.Opacity <= 0f )
				{
					_cellsToUnload.Add( cell.Index );
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

		foreach ( var level in _levels )
		{
			foreach ( var cell in level.Cells.Values )
			{
				cell.UpdateOpacity();

				(cell.IsMaskedByChild, cell.IsChildOutOfRange) = GetChildState( cell.Index );
			}
		}
	}
}
