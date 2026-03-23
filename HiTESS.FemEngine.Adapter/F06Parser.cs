using HiTESS.FemEngine.Adapter.Models;
using HiTESS.FemEngine.Core.Entities;
using System;
using System.Collections.Generic;
using System.IO;

namespace HiTESS.FemEngine.Adapter
{
  public static class F06Parser
  {
    public static (double MaxStress, double MaxDisp, List<NodeResultData> NodeResults, List<ElementResultData> ElemResults) Parse(string f06Path, FeModelContext context)
    {
      var nodeResults = new List<NodeResultData>();
      var elemResults = new List<ElementResultData>();
      double maxStress = 0.0;
      double maxDisp = 0.0;

      if (!File.Exists(f06Path)) return (0, 0, nodeResults, elemResults);

      var lines = File.ReadAllLines(f06Path);
      bool inDisp = false;
      bool inStress = false;

      foreach (var line in lines)
      {
        if (line.Contains("PAGE") || line.Contains("MAXIMUM")) { inDisp = false; inStress = false; }
        if (line.Contains("D I S P L A C E M E N T   V E C T O R")) { inDisp = true; inStress = false; continue; }
        if (line.Contains("S T R E S S E S   I N   B E A M   E L E M E N T S   ( C B E A M )")) { inStress = true; inDisp = false; continue; }

        var tokens = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        // 처짐 파싱 (Z축 처짐은 T3)
        if (inDisp && tokens.Length >= 6 && tokens[1] == "G")
        {
          if (int.TryParse(tokens[0], out int nid) && double.TryParse(tokens[4], out double t3))
          {
            if (Math.Abs(t3) > Math.Abs(maxDisp)) maxDisp = t3;

            // 컨텍스트에서 실제 X 좌표를 가져와 프론트엔드 매핑을 편하게 해줍니다.
            double xCoord = context.Nodes.Contains(nid) ? context.Nodes[nid].X : 0;
            nodeResults.Add(new NodeResultData { NodeId = nid, X = xCoord, DispZ = t3 });
          }
        }

        // 응력 파싱 (임시 단순 추출)
        if (inStress && tokens.Length >= 6)
        {
          if (int.TryParse(tokens[0], out int eid))
          {
            double localMaxStress = 0.0;
            foreach (var token in tokens)
            {
              if ((token.Contains("E") || token.Contains(".")) && double.TryParse(token, out double val))
              {
                if (Math.Abs(val) > localMaxStress) localMaxStress = Math.Abs(val);
              }
            }
            if (localMaxStress > maxStress) maxStress = localMaxStress;
            elemResults.Add(new ElementResultData { ElementId = eid, MaxStress = localMaxStress });
          }
        }
      }

      return (maxStress, maxDisp, nodeResults, elemResults);
    }
  }
}