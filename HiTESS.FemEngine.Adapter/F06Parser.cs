using HiTESS.FemEngine.Adapter.Models;
using HiTESS.FemEngine.Core.Entities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace HiTESS.FemEngine.Adapter
{
  /// <summary>
  /// Nastran 결과 파일(.f06) 스트리밍 파서
  /// </summary>
  public static class F06Parser
  {
    private enum Section { None, Displacement, Stress, Force }

    /// <summary>
    /// .f06 파일을 스트리밍 방식으로 읽어 변위·응력·내력 결과를 파싱합니다.
    /// </summary>
    public static (double MaxStress, double MaxDisp,
                   List<NodeResultData> NodeResults,
                   List<ElementResultData> StressResults,
                   List<BeamForceData> ForceResults)
      Parse(string f06Path, FeModelContext context)
    {
      var nodeResults   = new List<NodeResultData>();
      var stressResults = new List<ElementResultData>();
      var forceResults  = new List<BeamForceData>();
      double maxStress = 0.0;
      double maxDisp   = 0.0;

      if (!File.Exists(f06Path))
        return (0, 0, nodeResults, stressResults, forceResults);

      var section = Section.None;
      int lastEid = -1;   // 페이지 분리 시에도 유지

      foreach (var line in File.ReadLines(f06Path))
      {
        // 페이지 전환: 섹션 상태만 초기화, lastEid는 유지 (요소가 페이지에 걸쳐 출력되는 경우 대응)
        if (line.Contains("PAGE") || line.Contains("MAXIMUM"))
        {
          section = Section.None;
          continue;
        }

        if (line.Contains("D I S P L A C E M E N T   V E C T O R"))
        {
          section = Section.Displacement;
          continue;
        }

        if (line.Contains("S T R E S S E S   I N   B E A M   E L E M E N T S"))
        {
          section = Section.Stress;
          continue;
        }

        if (line.Contains("F O R C E S   I N   B E A M   E L E M E N T S"))
        {
          section = Section.Force;
          continue;
        }

        var tokens = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) continue;

        // ── 1. 변위 파싱 ────────────────────────────────────────────────────────
        // 형식: NODE_ID  G  T1  T2  T3  R1  R2  R3
        if (section == Section.Displacement &&
            tokens.Length >= 6 && tokens[1] == "G" &&
            int.TryParse(tokens[0], out int nid) &&
            ParseDouble(tokens[4], out double t3))
        {
          if (Math.Abs(t3) > Math.Abs(maxDisp)) maxDisp = t3;
          double xCoord = context.Nodes.Contains(nid) ? context.Nodes[nid].X : 0;
          nodeResults.Add(new NodeResultData { NodeId = nid, X = xCoord, DispZ = t3 });
          continue;
        }

        // ── 공통: 요소 ID 선언 라인 ─────────────────────────────────────────────
        // 형식: "0  EID"  (토큰 2개, 첫 토큰 "0", 두 번째 토큰 양수 정수)
        if ((section == Section.Stress || section == Section.Force) &&
            tokens.Length == 2 && tokens[0] == "0" &&
            int.TryParse(tokens[1], out int announcedEid) && announcedEid > 0)
        {
          lastEid = announcedEid;
          continue;
        }

        // ── 2. CBEAM 응력 파싱 ──────────────────────────────────────────────────
        // 형식: GRID  DIST  SXC  SXD  SXE  SXF  S-MAX  S-MIN  [M.S.-T  M.S.-C]
        if (section == Section.Stress && lastEid > 0 && tokens.Length >= 8 &&
            int.TryParse(tokens[0], out _) &&
            ParseDouble(tokens[1], out double sDist) &&
            ParseDouble(tokens[2], out double sxc)  &&
            ParseDouble(tokens[3], out double sxd)  &&
            ParseDouble(tokens[4], out double sxe)  &&
            ParseDouble(tokens[5], out double sxf)  &&
            ParseDouble(tokens[6], out double sMax)  &&
            ParseDouble(tokens[7], out double sMin))
        {
          double localMax = Math.Max(Math.Abs(sMax), Math.Abs(sMin));
          if (localMax > maxStress) maxStress = localMax;

          stressResults.Add(new ElementResultData
          {
            ElementId = lastEid,
            Dist      = sDist,
            SXC       = sxc,
            SXD       = sxd,
            SXE       = sxe,
            SXF       = sxf,
            SMax      = sMax,
            SMin      = sMin,
            MaxStress = localMax
          });
          continue;
        }

        // ── 3. CBEAM 내력 파싱 ──────────────────────────────────────────────────
        // 형식: GRID  DIST  BM1  BM2  SF1  SF2  AXIAL  TORQUE  WARPING
        if (section == Section.Force && lastEid > 0 && tokens.Length >= 9 &&
            int.TryParse(tokens[0], out _) &&
            ParseDouble(tokens[1], out double fDist) &&
            ParseDouble(tokens[2], out double bm1)   &&
            ParseDouble(tokens[3], out double bm2)   &&
            ParseDouble(tokens[4], out double sf1)   &&
            ParseDouble(tokens[5], out double sf2)   &&
            ParseDouble(tokens[6], out double axial) &&
            ParseDouble(tokens[7], out double torque) &&
            ParseDouble(tokens[8], out double warping))
        {
          forceResults.Add(new BeamForceData
          {
            ElementId      = lastEid,
            Dist           = fDist,
            BendingMoment1 = bm1,
            BendingMoment2 = bm2,
            ShearForce1    = sf1,
            ShearForce2    = sf2,
            AxialForce     = axial,
            Torque         = torque,
            WarpingTorque  = warping
          });
        }
      }

      return (maxStress, maxDisp, nodeResults, stressResults, forceResults);
    }

    private static bool ParseDouble(string s, out double val) =>
      double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out val);
  }
}
