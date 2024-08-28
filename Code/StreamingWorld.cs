using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Sandbox.Worlds;

public sealed class StreamingWorld : Component, Component.ExecuteInEditor
{
	private readonly Dictionary<Vector3Int, WorldCell> _cells = new();

	private readonly HashSet<Vector3Int> _loadOrigins = new();
	private readonly HashSet<Vector3Int> _cellsToLoad = new();
	private readonly HashSet<Vector3Int> _cellsToUnload = new();

	private Transform? _editorCameraTransform;
	private StreamingWorld? _parent;

	[Property]
	public StreamingWorld? Parent
	{
		get => _parent;
		set
		{
			if ( value?.Hierarchy.Contains( this ) is true )
			{
				throw new ArgumentException( "World hierarchy must have a root." );
			}

			if ( _parent is not null )
			{
				_parent.CellReady -= Parent_CellReady;
				_parent.CellUnloaded -= Parent_CellUnloaded;
			}

			_parent = value;

			if ( _parent is not null )
			{
				_parent.CellReady += Parent_CellReady;
				_parent.CellUnloaded += Parent_CellUnloaded;
			}
		}
	}

	public bool HasParent => Parent.IsValid();

	[Property, JsonIgnore, ShowIf( nameof(HasParent), true )]
	public int Level => Parent is { IsValid: true } parent ? parent.Level + 1 : 0;

	[Property, HideIf( nameof(HasParent), true )]
	public bool Is2D { get; set; }

	[Property, HideIf( nameof(HasParent), true )]
	public float CellSize { get; set; } = 1024f;

	[Property, HideIf( nameof(Is2D), true ), HideIf( nameof(HasParent), true )]
	public float CellHeight { get; set; } = 1024f;

	/// <summary>
	/// How many cells away from a <see cref="LoadOrigin"/> should be loaded.
	/// </summary>
	[Property] public int LoadRadius { get; set; } = 4;

	internal event Action<Vector3Int>? CellReady;
	internal event Action<Vector3Int>? CellUnloaded;

	private void UpdateDimensions()
	{
		if ( Parent is { IsValid: true } parent )
		{
			Is2D = parent.Is2D;
			CellHeight = parent.CellHeight;
			CellSize = parent.CellSize * 2f;
		}
	}

	protected override void OnDestroy()
	{
		if ( _parent is not null )
		{
			_parent.CellReady += Parent_CellReady;
			_parent.CellUnloaded += Parent_CellUnloaded;
		}
	}

	protected override void OnValidate()
	{
		UpdateDimensions();
	}

	protected override void OnUpdate()
	{
		UpdateDimensions();

		_loadOrigins.Clear();

		_cellsToLoad.Clear();
		_cellsToUnload.Clear();

		if ( !Game.IsEditor || Game.IsPlaying || Game.IsPaused )
		{
			foreach ( var origin in Scene.GetAllComponents<LoadOrigin>() )
			{
				_loadOrigins.Add( GetCellIndex( origin.Transform.Position ) );
			}
		}

		if ( Game.IsEditor && !Game.IsPlaying && _editorCameraTransform is { Position: { } editorCamPos } )
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

	private bool ShouldEnableCell( Vector3Int cellIndex ) => !AreParentCellsReady( cellIndex );

	public CellState GetCellState( Vector3Int cellIndex )
	{
		return _cells.TryGetValue( cellIndex, out var cell ) && cell is { IsValid: true }
			? cell.State
			: CellState.Unloaded;
	}

	private bool AreParentCellsReady( Vector3Int cellIndex )
	{
		if ( Parent is not { IsValid: true } parent )
		{
			return false;
		}

		var parentIndex = ToParentCellIndex( cellIndex );

		for ( var z = 0; z < (Is2D ? 1 : 2); z++ )
		for ( var y = 0; y < 2; y++ )
		for ( var x = 0; x < 2; x++ )
		{
			if ( parent.GetCellState( parentIndex + new Vector3Int( x, y, z ) ) == CellState.Ready ) continue;

			return false;
		}

		return true;
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

		cell.GameObject.Enabled = ShouldEnableCell( cellIndex );

		Scene.GetAllComponents<ICellLoader>().FirstOrDefault()?.LoadCell( cell );

		if ( cell.State != CellState.Loading )
		{
			cell.MarkReady();
		}
	}

	private void UnloadCell( Vector3Int cellIndex )
	{
		if ( !_cells.Remove( cellIndex, out var cell ) ) return;

		Scene.GetAllComponents<ICellLoader>().FirstOrDefault()?.UnloadCell( cell );
		CellUnloaded?.Invoke( cellIndex );

		cell.GameObject.Destroy();
	}

	private static Vector3Int FromParentCellIndex( Vector3Int parentCellIndex )
	{
		return (parentCellIndex - 1) / 2;
	}

	private static Vector3Int ToParentCellIndex( Vector3Int cellIndex )
	{
		return cellIndex * 2;
	}

	internal void DispatchCellReady( Vector3Int cellIndex )
	{
		CellReady?.Invoke( cellIndex );
	}

	private void Parent_CellReady( Vector3Int parentCellIndex )
	{
		var cellIndex = FromParentCellIndex( parentCellIndex );

		if ( !_cells.TryGetValue( cellIndex, out var cell ) ) return;
		if ( !cell.GameObject.Enabled ) return;
		if ( ShouldEnableCell( cellIndex ) ) return;

		cell.GameObject.Enabled = false;
	}

	private void Parent_CellUnloaded( Vector3Int parentCellIndex )
	{
		var cellIndex = FromParentCellIndex( parentCellIndex );

		if ( !_cells.TryGetValue( cellIndex, out var cell ) ) return;
		if ( cell.GameObject.Enabled ) return;
		if ( !ShouldEnableCell( cellIndex ) ) return;

		cell.GameObject.Enabled = true;
	}

	protected override void DrawGizmos()
	{
		_editorCameraTransform = Gizmo.CameraTransform;

		if ( !Gizmo.IsSelected ) return;

		Gizmo.Draw.Color = new ColorHsv( Level * 30f, 1f, 1f, 0.25f );

		foreach ( var (index, cell) in _cells )
		{
			Gizmo.Transform = cell.Transform.World;

			if ( AreParentCellsReady( index ) )
			{
				Gizmo.Draw.SolidBox( new BBox( 0f, new Vector3( CellSize, CellSize, Is2D ? 2048f : CellHeight ) ) );
			}
			else
			{
				Gizmo.Draw.LineBBox( new BBox( 0f, new Vector3( CellSize, CellSize, Is2D ? 2048f : CellHeight ) ) );
			}
		}
	}

	private IEnumerable<StreamingWorld> Hierarchy
	{
		get
		{
			yield return this;

			if ( Parent is null ) yield break;

			foreach ( var parent in Parent.Hierarchy )
			{
				yield return parent;
			}
		}
	}
}
