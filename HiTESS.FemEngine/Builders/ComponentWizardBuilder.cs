using System;
using System.Collections.Generic;
using System.Linq;
using HiTESS.FemEngine.Core.Entities;

namespace HiTESS.FemEngine.Core.Builders
{
  public class ComponentWizardBuilder
  {
    public FeModelContext BuildModel(
        string beamType,
        double length,
        double[] rawDims,
        List<(double Pos, string Type)> boundaries,
        List<(double Pos, double Magnitude)> loads)
    {
      var context = FeModelContext.CreateEmpty();

      int matId = context.Materials.AddOrGet("Steel", 206000, 0.3, 7.85e-09);

      // ====================================================================
      // ★ 치수 매핑 (Nastran 규격에 맞게 배열 Editing 및 수학적 변환)
      // ====================================================================
      double[] mappedDims = MapDimensionsForNastran(beamType, rawDims);

      int propId = context.Properties.AddOrGet(beamType, mappedDims, matId);

      var keyPositions = new HashSet<double> { 0.0, length };
      foreach (var bc in boundaries) keyPositions.Add(bc.Pos);
      foreach (var load in loads) keyPositions.Add(load.Pos);

      var sortedPos = keyPositions.OrderBy(x => x).ToList();
      var nodeDict = new Dictionary<double, int>();

      foreach (double x in sortedPos)
      {
        nodeDict[x] = context.Nodes.AddOrGet(x, 0.0, 0.0);
      }

      double[] defaultOri = { 0.0, 0.0, 1.0 };
      for (int i = 0; i < sortedPos.Count - 1; i++)
      {
        int n1 = nodeDict[sortedPos[i]];
        int n2 = nodeDict[sortedPos[i + 1]];

        context.Elements.AddNew(new List<int> { n1, n2 }, propId, defaultOri);
      }

      return context;
    }

    private double[] MapDimensionsForNastran(string beamType, double[] rawDims)
    {
      // 프론트엔드 입력 기준:
      double w = rawDims.Length > 0 ? rawDims[0] : 0; // dim1 (Width 또는 Diameter)
      double h = rawDims.Length > 1 ? rawDims[1] : 0; // dim2 (Height 또는 Thickness)
      double tf = rawDims.Length > 2 ? rawDims[2] : 0; // dim3 (Flange Thk)
      double tw = rawDims.Length > 3 ? rawDims[3] : 0; // dim4 (Web Thk)

      switch (beamType.ToUpper())
      {
        case "I":
          // ✅ 수정 완료: Nastran "I" 규격 -> [H, W1, W2, tw, t1, t2] 순서
          return new double[] { h, w, w, tw, tf, tf };

        case "H":
          // H-Beam: [W, tf*2, H, tw] 
          return new double[] { w, tf * 2.0, h, tw };

        case "ROD":
          // ROD: 직경(D) -> 반지름(R)으로 변환
          return new double[] { w / 2.0 };

        case "TUBE":
          // TUBE: 직경(D)과 두께(t) -> [바깥쪽 반지름(R_out), 안쪽 반지름(R_in)]
          double rOut = w / 2.0;
          double rIn = rOut - h;
          return new double[] { rOut, rIn };

        case "L":
        case "T":
          return new double[] { w, h, tf, tw };
        case "CHAN":
          // L, T, CHAN: [W, H, tf, tw] 순서 그대로
          return new double[] { w, h, tw, tf };

        case "BAR":
          return new double[] { w, h };

        default:
          return rawDims;
      }
    }
  }
}