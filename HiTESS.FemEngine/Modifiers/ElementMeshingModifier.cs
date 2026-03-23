using HiTESS.FemEngine.Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HiTESS.FemEngine.Core.Modifiers
{
  public static class ElementMeshingModifier
  {
    /// <summary>
    /// 요소를 사용자가 지정한 meshSize 이하가 되도록 균등 분할합니다.
    /// </summary>
    public static int Run(FeModelContext context, double meshSize, Action<string>? log = null)
    {
      if (meshSize <= 0) return 0;
      log ??= Console.WriteLine;

      int splitCount = 0;
      var elementIds = context.Elements.Keys.ToList();

      foreach (var eid in elementIds)
      {
        if (!context.Elements.Contains(eid)) continue;
        var e = context.Elements[eid];

        int n1 = e.NodeIDs.First();
        int n2 = e.NodeIDs.Last();
        var p1 = context.Nodes[n1];
        var p2 = context.Nodes[n2];

        double length = (p2 - p1).Magnitude();

        // meshSize보다 1.1배 이상 클 때만 분할 수행
        if (length > meshSize * 1.1)
        {
          int steps = (int)Math.Ceiling(length / meshSize);
          var dir = (p2 - p1).Normalize();
          double stepLen = length / steps;

          List<int> nodeChain = new List<int> { n1 };
          for (int i = 1; i < steps; i++)
          {
            var newNodePos = p1 + (dir * (stepLen * i));
            int newNid = context.Nodes.AddOrGet(newNodePos.X, newNodePos.Y, newNodePos.Z);
            nodeChain.Add(newNid);
          }
          nodeChain.Add(n2);

          var extra = e.ExtraData?.ToDictionary(k => k.Key, v => v.Value);
          int propId = e.PropertyID;
          var ori = e.Orientation;

          context.Elements.Remove(eid);

          for (int i = 0; i < nodeChain.Count - 1; i++)
          {
            context.Elements.AddNew(new List<int> { nodeChain[i], nodeChain[i + 1] }, propId, ori, extra);
          }
          splitCount++;
        }
      }

      log($"[Mesh] 총 {splitCount}개의 부재를 {meshSize}mm 간격으로 분할 완료했습니다.");
      return splitCount;
    }
  }
}