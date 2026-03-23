using HiTESS.FemEngine.Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HiTESS.FemEngine.Core.Entities
{
  /// <summary>
  /// FE 모델 전체 컨텍스트
  /// - 순수 데이터(Entity)들을 묶는 루트(Aggregate Root) 객체
  /// - Service / Modifier의 공통 접근점으로 활용됩니다.
  /// </summary>
  public sealed class FeModelContext
  {
    public Materials Materials { get; }
    public Properties Properties { get; }
    public Nodes Nodes { get; }
    public Elements Elements { get; }
    public Rigids Rigids { get; } = new Rigids();
    public PointMasses PointMasses { get; } = new PointMasses();
    public HashSet<int> WeldNodes { get; } = new HashSet<int>();

    public FeModelContext(
        Materials materials,
        Properties properties,
        Nodes nodes,
        Elements elements,
        Rigids rigids)
    {
      Materials = materials ?? throw new ArgumentNullException(nameof(materials));
      Properties = properties ?? throw new ArgumentNullException(nameof(properties));
      Nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
      Elements = elements ?? throw new ArgumentNullException(nameof(elements));
      Rigids = rigids ?? throw new ArgumentNullException(nameof(rigids));
    }

    /// <summary>
    /// 빈 FE 모델 컨텍스트를 생성합니다.
    /// </summary>
    public static FeModelContext CreateEmpty()
    {
      return new FeModelContext(
          new Materials(),
          new Properties(),
          new Nodes(),
          new Elements(),
          new Rigids()
      );
    }

    /// <summary>
    /// 노드가 병합(Collapse)될 때 용접점 ID를 매핑된 새로운 ID로 갈아끼웁니다.
    /// </summary>
    /// <param name="oldToRep">Key: 기존 노드 ID, Value: 대체될 새 노드 ID</param>
    public void RemapWeldNodes(IReadOnlyDictionary<int, int> oldToRep)
    {
      if (oldToRep == null || oldToRep.Count == 0) return;

      var oldNodes = WeldNodes.ToList();
      foreach (var oldNode in oldNodes)
      {
        if (oldToRep.TryGetValue(oldNode, out int newNode))
        {
          WeldNodes.Remove(oldNode);
          WeldNodes.Add(newNode); // 기존 용접점이 삭제되면 흡수된 새 노드에 용접 속성 이관
        }
      }
    }
  }
}