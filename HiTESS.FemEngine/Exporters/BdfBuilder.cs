using HiTESS.FemEngine.Core.Entities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HiTESS.FemEngine.Core.Exporter
{
  public class BdfBuilder
  {
    public int sol;
    public FeModelContext feModelContext;
    int LoadCase;
    public bool ForceRigid123456 { get; set; }
    public List<(int NodeID, string Type)> SpcList;
    public List<(int NodeID, double Magnitude)> ForceList;

    // BDF에 입력된 텍스트 라인모음 리스트
    public List<String> BdfLines = new List<String>();

    // 생성자 함수
    public BdfBuilder(
      int Sol,
      FeModelContext FeModelContext,
      List<(int NodeID, string Type)> spcList,
      List<(int NodeID, double Magnitude)> forceList)
    {
      this.sol = Sol;
      this.feModelContext = FeModelContext;
      this.SpcList = spcList ?? new List<(int, string)>();
      this.ForceList = forceList ?? new List<(int, double)>();
    }

    public void Run()
    {
      // Nastran 솔버 입력
      ExecutiveControlSection();

      // 출력결과 종류 설정, LoadCase 설정
      CaseControlSection();

      // Subcase 입력
      SubcaseSection();

      // Node, Element 데이터 입력
      NodeElementSection();

      // Property, Material 데이터 입력
      PropertyMaterialSection();

      // RBE 연결
      RigidElementSection();

      PointMassSection();

      // 경계 조건 데이터 입력
      BoundaryConditionSection();

      // 하중 입력
      LoadBulkSection();

      // 종료 시그니쳐
      EndSigniture();

    }

    public void ExecutiveControlSection()
    {
      BdfLines.Add(BdfFormatFields.FormatField($"SOL {this.sol}"));
      BdfLines.Add(BdfFormatFields.FormatField($"CEND"));
    }

    public void CaseControlSection()
    {
      BdfLines.Add("DISPLACEMENT = ALL");
      BdfLines.Add("FORCE = ALL");
      BdfLines.Add("SPCFORCES = ALL");
      BdfLines.Add("STRESS = ALL");
    }

    public void SubcaseSection()
    {
      BdfLines.Add("SUBCASE       1");
      BdfLines.Add("LABEL = LC1");
      BdfLines.Add("SPC = 1");
      BdfLines.Add("LOAD = 2");
      BdfLines.Add("ANALYSIS = STATICS");
      BdfLines.Add("BEGIN BULK");
      BdfLines.Add("PARAM,POST,-1");
      BdfLines.Add("PARAM,AUTOMPC,YES");
    }

    public void NodeElementSection()
    {
      foreach (var node in this.feModelContext.Nodes)
      {
        string nodeText = $"{BdfFormatFields.FormatField("GRID")}"
          + $"{BdfFormatFields.FormatField(node.Key, "right")}"
          + $"{BdfFormatFields.FormatField("")}"
          + $"{BdfFormatFields.FormatField(node.Value.X, "right")}"
          + $"{BdfFormatFields.FormatField(node.Value.Y, "right")}"
          + $"{BdfFormatFields.FormatField(node.Value.Z, "right")}";
        BdfLines.Add(nodeText);
      }

      foreach (var element in this.feModelContext.Elements)
      {
        double oriX = 0.0, oriY = 0.0, oriZ = 1.0;

        if (element.Value.ExtraData.TryGetValue("OriX", out string sX))
          double.TryParse(sX, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out oriX);
        if (element.Value.ExtraData.TryGetValue("OriY", out string sY))
          double.TryParse(sY, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out oriY);
        if (element.Value.ExtraData.TryGetValue("OriZ", out string sZ))
          double.TryParse(sZ, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out oriZ);
        string elementText = $"{BdfFormatFields.FormatField("CBEAM")}"
         + $"{BdfFormatFields.FormatField(element.Key, "right")}"
         + $"{BdfFormatFields.FormatField(element.Value.PropertyID, "right")}"
         + $"{BdfFormatFields.FormatField(element.Value.NodeIDs[0], "right")}"
         + $"{BdfFormatFields.FormatField(element.Value.NodeIDs[1], "right")}"
          + $"{BdfFormatFields.FormatField(oriX, "right")}" // X1 (oriX)
         + $"{BdfFormatFields.FormatField(oriY, "right")}" // X2 (oriY)
         + $"{BdfFormatFields.FormatField(oriZ, "right")}" // X3 (oriZ)
         + $"{BdfFormatFields.FormatField("BGG", "right")}";
        BdfLines.Add(elementText);
      }
    }

    public void PropertyMaterialSection()
    {
      foreach (var property in this.feModelContext.Properties)
      {
        string type = property.Value.Type.ToUpper();

        // 1. PBEAML 카드 첫 번째 줄
        // 필드 1: PBEAML
        // 필드 2: PID
        // 필드 3: MID
        // 필드 4: SO (빈칸 대신 "MSCBML0" 기입)
        // 필드 5: TYPE
        // 필드 6~9: 빈칸
        // 필드 10: + (Continuation Mark)
        string propertyText = $"{BdfFormatFields.FormatField("PBEAML")}"
          + $"{BdfFormatFields.FormatField(property.Key, "right")}"
          + $"{BdfFormatFields.FormatField(property.Value.MaterialID, "right")}"
          + $"{BdfFormatFields.FormatField("MSCBML0", "right")}" // ✅ 스승님 요청사항 반영 (단면 라이브러리 지정)
          + $"{BdfFormatFields.FormatField(type, "right")}"
          + $"{BdfFormatFields.FormatField("", "right")}"
          + $"{BdfFormatFields.FormatField("", "right")}"
          + $"{BdfFormatFields.FormatField("", "right")}"
          + $"{BdfFormatFields.FormatField("", "right")}"
          + $"{BdfFormatFields.FormatField("+", "right")}";

        BdfLines.Add(propertyText);

        // 2. 치수(Dimensions) 작성 줄
        // 첫 번째 필드를 '+'로 시작 (Nastran Continuation Mark)
        string dimText = $"{BdfFormatFields.FormatField("+", "left")}";

        // 배열 치수를 순서대로 8칸씩 매핑
        foreach (double d in property.Value.Dim)
        {
          dimText += $"{BdfFormatFields.FormatField(d, "right")}";
        }

        // 마지막 필드에 NSM(비구조 질량) 0.0 추가
        dimText += $"{BdfFormatFields.FormatField(0.0, "right")}";

        BdfLines.Add(dimText);
      }

      // 3. Material 데이터 출력
      foreach (var material in this.feModelContext.Materials)
      {
        string materialText = $"{BdfFormatFields.FormatField("MAT1")}"
            + $"{BdfFormatFields.FormatField(material.Key, "right")}"
            + $"{BdfFormatFields.FormatField(material.Value.E, "right")}"
            + $"{BdfFormatFields.FormatField("", "right")}"
            + $"{BdfFormatFields.FormatField(material.Value.Nu, "right")}"
            + $"{BdfFormatFields.FormatField(material.Value.Rho, "right", true)}";

        BdfLines.Add(materialText);
      }
    }

    public void RigidElementSection()
    {
      if (this.feModelContext.Rigids == null || this.feModelContext.Rigids.Count == 0) return;

      foreach (var kv in this.feModelContext.Rigids)
      {
        int eid = kv.Key;      // 9000001 부터 시작하는 고유 ID
        var info = kv.Value;

        // Dependent Node가 없으면 RBE2 생성 불가하므로 스킵
        if (info.DependentNodeIDs == null || info.DependentNodeIDs.Count == 0) continue;

        // -----------------------------------------------------------------------
        // Nastran RBE2 Format Logic (Small Field)
        // Row 1: [RBE2][EID][GN][CM][GM1][GM2][GM3][GM4][GM5][+]
        // Row 2: [+][GM6][GM7]...
        // -----------------------------------------------------------------------
        var sb = new StringBuilder();

        // 1. 첫 번째 줄 헤더 작성 (필드 1~4 사용)
        sb.Append(BdfFormatFields.FormatField("RBE2"));
        sb.Append(BdfFormatFields.FormatField(eid, "right"));
        sb.Append(BdfFormatFields.FormatField(info.IndependentNodeID, "right"));
        sb.Append(BdfFormatFields.FormatField(info.Cm, "right")); // DOF 고정 (123456)

        // 현재 줄에서 사용된 필드 수 (1~4번 필드 사용됨)
        int fieldsUsed = 4;

        // 2. Dependent Nodes (GMi) 순회
        for (int i = 0; i < info.DependentNodeIDs.Count; i++)
        {
          int depNodeID = info.DependentNodeIDs[i];

          // 필드 9번까지 꽉 찼다면 줄바꿈 처리 (필드 10은 연속 마크용)
          if (fieldsUsed >= 9)
          {
            sb.Append(BdfFormatFields.FormatField("+")); // 필드 10: Continuation Mark
            BdfLines.Add(sb.ToString());

            // StringBuilder 리셋 및 다음 줄 초기화
            sb.Clear();
            sb.Append(BdfFormatFields.FormatField("+")); // 다음 줄 필드 1: Continuation Mark Match
            fieldsUsed = 1; // 필드 1 사용됨
          }

          // 노드 ID 추가
          sb.Append(BdfFormatFields.FormatField(depNodeID, "right"));
          fieldsUsed++;
        }

        // 마지막 줄이 남아있다면 리스트에 추가
        if (sb.Length > 0)
        {
          BdfLines.Add(sb.ToString());
        }
      }
    }

    // ★ [신규 추가] 클래스 하단에 메써드 추가
    public void PointMassSection()
    {
      if (this.feModelContext.PointMasses == null || this.feModelContext.PointMasses.Count == 0) return;

      foreach (var pm in this.feModelContext.PointMasses)
      {
        // ---------------------------------------------------------
        // Nastran CONM2 Card Format (Small Field)
        // 1. Keyword : "CONM2"
        // 2. EID     : Element ID (Mass ID)
        // 3. G       : Grid ID (Node ID)
        // 4. CID     : Coordinate System (0 고정)
        // 5. M       : Mass Value (질량)
        // ---------------------------------------------------------
        string massLine = $"{BdfFormatFields.FormatField("CONM2")}"
                        + $"{BdfFormatFields.FormatField(pm.Key, "right")}"
                        + $"{BdfFormatFields.FormatField(pm.Value.NodeID, "right")}"
                        + $"{BdfFormatFields.FormatField(0, "right")}"
                        + $"{BdfFormatFields.FormatField(pm.Value.Mass.ToString("F3"), "right")}";

        BdfLines.Add(massLine);
      }
    }

    public void BoundaryConditionSection()
    {
      foreach (var spc in this.SpcList)
      {
        // Fix = 123456, Hinge = 123
        string dof = spc.Type.Equals("Fix", StringComparison.OrdinalIgnoreCase) ? "123456" : "123";

        string spcLine = $"{BdfFormatFields.FormatField("SPC")}"
                       + $"{BdfFormatFields.FormatField(1, "right")}"
                       + $"{BdfFormatFields.FormatField(spc.NodeID, "right")}"
                       + $"{BdfFormatFields.FormatField(dof, "right")}"
                       + $"{BdfFormatFields.FormatField(0.0, "right")}";

        BdfLines.Add(spcLine);
      }
    }

    public void LoadBulkSection()
    {
      foreach (var force in this.ForceList)
      {
        // UI에서 입력된 하중값(Mag)을 -Z 방향으로 가합니다.
        string forceLine = $"{BdfFormatFields.FormatField("FORCE")}"
                         + $"{BdfFormatFields.FormatField(2, "right")}" // SID=2 (Subcase에 맞춰짐)
                         + $"{BdfFormatFields.FormatField(force.NodeID, "right")}"
                         + $"{BdfFormatFields.FormatField(0, "right")}" // CID=0
                         + $"{BdfFormatFields.FormatField(force.Magnitude, "right")}" // Scale(Mag)
                         + $"{BdfFormatFields.FormatField(0.0, "right")}" // N1
                         + $"{BdfFormatFields.FormatField(0.0, "right")}" // N2
                         + $"{BdfFormatFields.FormatField(-1.0, "right")}"; // N3 (-Z 방향)

        BdfLines.Add(forceLine);
      }
    }

    public void EndSigniture()
    {
      BdfLines.Add("ENDDATA");
    }
  }
}