using System;
using System.Linq;
using HiTESS.FemEngine.Core.Entities;

namespace HiTESS.FemEngine.Core.Utils
{
  /// <summary>
  /// 요소의 물리적 단면(Property) 속성으로부터 치수 정보를 계산하고 추출하는 유틸리티 클래스입니다.
  /// </summary>
  public static class PropertyDimensionHelper
  {
    /// <summary>
    /// 주어진 Property의 형상(Type)을 기반으로 가장 큰 단면 치수(반경 또는 폭/높이)를 반환합니다.
    /// 교차 및 연장(Extend) 허용 오차 계산 시 사용됩니다.
    /// </summary>
    public static double GetMaxCrossSectionDim(Property prop)
    {
      var dim = prop.Dim;
      if (dim == null || dim.Count == 0) return 0.0;

      string type = prop.Type.ToUpper();
      return type switch
      {
        "L" => Math.Max(dim.ElementAtOrDefault(0), dim.ElementAtOrDefault(1)),
        "H" => Math.Max(dim.ElementAtOrDefault(0), dim.ElementAtOrDefault(2)),
        "TUBE" => dim.ElementAtOrDefault(0),
        "ROD" => dim.ElementAtOrDefault(0),
        "BAR" => Math.Max(dim.ElementAtOrDefault(0), dim.ElementAtOrDefault(1)),
        "CHAN" => dim.Max(),
        _ => dim.Max()
      };
    }

    /// <summary>
    /// Nastran 매핑된 치수 배열(Property.Dim)을 기반으로 단면적을 계산합니다.
    /// </summary>
    public static double ComputeArea(Property prop)
    {
      var d = prop.Dim;
      double D(int i) => d.Count > i ? d[i] : 0.0;

      return prop.Type.ToUpper() switch
      {
        // [H, W1, W2, tw, t1, t2]
        "I" => D(1)*D(4) + D(2)*D(5) + (D(0)-D(4)-D(5))*D(3),
        // [W, 2tf, H, tw]
        "H" => D(0)*D(1) + (D(2)-D(1))*D(3),
        // [R]
        "ROD" => Math.PI * D(0) * D(0),
        // [R_out, R_in]
        "TUBE" => Math.PI * (D(0)*D(0) - D(1)*D(1)),
        // [W, H]
        "BAR" => D(0) * D(1),
        // [W, H, tf, tw]
        "L" or "T" => D(0)*D(2) + (D(1)-D(2))*D(3),
        // [W, H, tw, tf]
        "CHAN" => 2*D(0)*D(3) + (D(1)-2*D(3))*D(2),
        _ => 0.0
      };
    }

    /// <summary>
    /// Nastran 매핑된 치수 배열(Property.Dim)을 기반으로 단면 2차 모멘트(Izz)를 계산합니다.
    /// </summary>
    public static double ComputeMomentOfInertia(Property prop)
    {
      var d = prop.Dim;
      double D(int i) => d.Count > i ? d[i] : 0.0;

      return prop.Type.ToUpper() switch
      {
        // [H, W1, W2, tw, t1, t2]
        "I" => (D(1)*Pow3(D(0)) - (D(1)-D(3))*Pow3(D(0)-D(4)-D(5))) / 12.0,
        // [W, 2tf, H, tw]
        "H" => (D(0)*Pow3(D(2)) - (D(0)-D(3))*Pow3(D(2)-D(1))) / 12.0,
        // [R]
        "ROD" => Math.PI * Pow4(D(0)) / 4.0,
        // [R_out, R_in]
        "TUBE" => Math.PI * (Pow4(D(0)) - Pow4(D(1))) / 4.0,
        // [W, H]
        "BAR" => D(0) * Pow3(D(1)) / 12.0,
        // [W, H, tf, tw]
        "L" => ComputeLAngleIzz(d),
        "T" => ComputeTSectionIzz(d),
        // [W, H, tw, tf]
        "CHAN" => (D(0)*Pow3(D(1)) - (D(0)-D(2))*Pow3(D(1)-2*D(3))) / 12.0,
        _ => 0.0
      };
    }

    // L형강 [W, H, tf, tw]: 무게중심 기준 Izz (평행축 정리 적용)
    private static double ComputeLAngleIzz(IReadOnlyList<double> d)
    {
      double W = d.Count > 0 ? d[0] : 0, H = d.Count > 1 ? d[1] : 0;
      double tf = d.Count > 2 ? d[2] : 0, tw = d.Count > 3 ? d[3] : 0;
      double area = W * tf + (H - tf) * tw;
      if (area <= 0) return 0;
      double izzBottom = (tw * Pow3(H) + (W - tw) * Pow3(tf)) / 3.0;
      double ybar = (tw * H * H / 2.0 + (W - tw) * tf * tf / 2.0) / area;
      return izzBottom - area * ybar * ybar;
    }

    // T형강 [W, H, tf, tw]: 플랜지 상단 기준, 무게중심 기준 Izz (평행축 정리 적용)
    private static double ComputeTSectionIzz(IReadOnlyList<double> d)
    {
      double W = d.Count > 0 ? d[0] : 0, H = d.Count > 1 ? d[1] : 0;
      double tf = d.Count > 2 ? d[2] : 0, tw = d.Count > 3 ? d[3] : 0;
      double hw = H - tf;
      double area = W * tf + hw * tw;
      if (area <= 0) return 0;
      double izzBottom = (W * Pow3(H) - (W - tw) * Pow3(hw)) / 3.0;
      double ybar = (W * tf * (H - tf / 2.0) + hw * tw * (hw / 2.0)) / area;
      return izzBottom - area * ybar * ybar;
    }

    private static double Pow3(double v) => v * v * v;
    private static double Pow4(double v) => v * v * v * v;
  }
}