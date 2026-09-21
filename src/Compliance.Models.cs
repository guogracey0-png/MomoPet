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
    public class ComplianceRuleData
    {
        public string Id { get; set; } public string Term { get; set; } public string Category { get; set; } public string Severity { get; set; }
        public string Reason { get; set; } public string Suggestion { get; set; } public string RequiredWarning { get; set; }
    }

    public class ComplianceFindingData
    {
        public string Id { get; set; } public string RuleId { get; set; } public string Text { get; set; } public int Start { get; set; } public int Length { get; set; }
        public string Category { get; set; } public string Severity { get; set; } public string Reason { get; set; } public string Suggestion { get; set; }
        public string RequiredWarning { get; set; } public string ModelComment { get; set; } public string Disposition { get; set; }
        public string CoverageNote { get; set; }
    }

    public class ComplianceAuditRecord
    {
        public string Id { get; set; } public string ContentHash { get; set; } public string RulebookVersion { get; set; } public string Model { get; set; }
        public string Provider { get; set; } public string PromptVersion { get; set; } public string ReviewedAt { get; set; } public string Reviewer { get; set; }
        public string SourceName { get; set; } public string SourceReference { get; set; } public string OriginalText { get; set; } public string ModelResult { get; set; }
        public string Conclusion { get; set; } public List<ComplianceFindingData> Findings { get; set; } public List<ComplianceFindingData> CoveredFindings { get; set; }
    }

    public class ComplianceModelReview
    {
        public string conclusion { get; set; } public string summary { get; set; } public List<ComplianceModelReviewItem> items { get; set; }
    }

    public class ComplianceModelReviewItem
    {
        public string hit_id { get; set; } public string excerpt { get; set; } public string rule_id { get; set; } public string decision { get; set; }
        public string category { get; set; } public string severity { get; set; } public string reason { get; set; } public string suggestion { get; set; } public double confidence { get; set; }
    }
}

