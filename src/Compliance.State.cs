using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MomoPetApp
{
    public partial class PetController
    {

        const string CompliancePromptVersion="2026-09-10-v4-structured-review";

        Window compliancePanel,complianceHistoryPanel;

        TextBox complianceInput,complianceResult,complianceReviewerBox,complianceReferenceBox;

        RichTextBox complianceOriginalView;

        TextBlock complianceStatus,complianceSource,complianceSummary,complianceFindingDetail;

        Image compliancePreview;

        ComboBox complianceModelBox;

        ListBox complianceFindingList,complianceHistoryList;

        Button complianceLocalTab,complianceModelTab;

        Grid complianceLocalPanel,complianceModelPanel;

        readonly Dictionary<string,Run> complianceMarkRuns=new Dictionary<string,Run>();

        string complianceImagePath,complianceAuditFile,complianceRulebookVersion="未知版本",complianceSourceName="手动输入";

        readonly List<ComplianceRuleData> complianceRuleDefinitions=new List<ComplianceRuleData>();

        readonly List<ComplianceFindingData> currentComplianceFindings=new List<ComplianceFindingData>();

        readonly List<ComplianceFindingData> currentComplianceCoveredFindings=new List<ComplianceFindingData>();

        readonly List<ComplianceAuditRecord> complianceAudits=new List<ComplianceAuditRecord>();

        ComplianceAuditRecord currentComplianceAudit;
}
}
