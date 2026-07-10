using Photon.Core.Models;

namespace Photon.App.Forms;

public partial class BatchRenameForm
{
    /// <summary>Byte multipliers matching the B / KB / MB / GB unit combos.</summary>
    private static readonly long[] SizeUnitMultipliers = [1L, 1L << 10, 1L << 20, 1L << 30];

    /// <summary>Switches between the simple and advanced option stacks, carrying shared values across.</summary>
    private void SetMode(bool advanced)
    {
        if (advanced != _advancedMode)
        {
            _suspendPreview = true;
            if (advanced) CopySimpleToAdvanced();
            else CopyAdvancedToSimple();
            _suspendPreview = false;
        }
        _advancedMode = advanced;
        _advancedHost.Visible = advanced;
        _simpleHost.Visible = !advanced;
        _chkAdvancedToggle.Text = advanced
            ? "Simple mode — back to basics"
            : "Advanced mode — full control center";
        if (_chkAdvancedToggle.Checked != advanced) _chkAdvancedToggle.Checked = advanced;
        RequestPreview();
    }

    private void CopySimpleToAdvanced()
    {
        _txtAdvPattern.Text = _txtSimplePattern.Text;
        _txtAdvPrefix.Text = _txtSimplePrefix.Text;
        _txtAdvSuffix.Text = _txtSimpleSuffix.Text;
        _cmbAdvNameCase.SelectedIndex = _cmbSimpleNameCase.SelectedIndex;
        _cmbAdvExtCase.SelectedIndex = _cmbSimpleExtCase.SelectedIndex;
        _numAdvStart.Value = _numSimpleStart.Value;
        _numAdvPad.Value = _numSimplePad.Value;
        // Seed the rule list from the simple find/replace row, but never clobber existing rules.
        if (_txtSimpleFind.Text.Length > 0 && _rulesGrid.Rows.Count == 0)
            _rulesGrid.Rows.Add(true, _txtSimpleFind.Text, _txtSimpleReplace.Text, false, false, false, false, "Name");
        ValidateRules();
    }

    private void CopyAdvancedToSimple()
    {
        _txtSimplePattern.Text = _txtAdvPattern.Text;
        _txtSimplePrefix.Text = _txtAdvPrefix.Text;
        _txtSimpleSuffix.Text = _txtAdvSuffix.Text;
        _cmbSimpleNameCase.SelectedIndex = _cmbAdvNameCase.SelectedIndex;
        _cmbSimpleExtCase.SelectedIndex = _cmbAdvExtCase.SelectedIndex;
        _numSimpleStart.Value = _numAdvStart.Value;
        _numSimplePad.Value = _numAdvPad.Value;
        foreach (DataGridViewRow row in _rulesGrid.Rows)
        {
            if (row.Cells[RuleColOn].Value is not true) continue;
            _txtSimpleFind.Text = row.Cells[RuleColFind].Value as string ?? "";
            _txtSimpleReplace.Text = row.Cells[RuleColReplace].Value as string ?? "";
            break;
        }
    }

    /// <summary>Reads the active mode's controls into a fresh RenameOptions for planning/execution.</summary>
    private RenameOptions CollectOptions()
    {
        var o = new RenameOptions
        {
            IncludeMask = _txtIncludeMask.Text.Trim(),
            ExcludeMask = _txtExcludeMask.Text.Trim(),
            IncludeSubfolders = _chkSubfolders.Checked,
        };

        if (!_advancedMode)
        {
            o.Pattern = string.IsNullOrWhiteSpace(_txtSimplePattern.Text) ? "{name}" : _txtSimplePattern.Text;
            if (_txtSimpleFind.Text.Length > 0)
                o.Replacements = [new FindReplaceRule { Find = _txtSimpleFind.Text, Replace = _txtSimpleReplace.Text }];
            o.Prefix = _txtSimplePrefix.Text;
            o.Suffix = _txtSimpleSuffix.Text;
            o.NameCase = CaseFromIndex(_cmbSimpleNameCase.SelectedIndex);
            o.ExtensionCase = CaseFromIndex(_cmbSimpleExtCase.SelectedIndex);
            o.CounterStart = (int)_numSimpleStart.Value;
            o.CounterPadding = (int)_numSimplePad.Value;
            return o;
        }

        // ----- Pattern & numbering -----
        o.Pattern = string.IsNullOrWhiteSpace(_txtAdvPattern.Text) ? "{name}" : _txtAdvPattern.Text;
        o.DateSource = (DateSource)Math.Max(0, _cmbDateSource.SelectedIndex);
        o.CounterStart = (int)_numAdvStart.Value;
        o.CounterStep = (int)_numAdvStep.Value;
        o.CounterPadding = (int)_numAdvPad.Value;
        o.CounterPerFolder = _chkCounterPerFolder.Checked;
        o.CounterStyle = (CounterStyle)Math.Max(0, _cmbCounterStyle.SelectedIndex);
        o.NumberingOrder = (NumberingOrder)Math.Max(0, _cmbNumberingOrder.SelectedIndex);
        o.Counter2Start = (int)_numCounter2Start.Value;
        o.Counter2Step = (int)_numCounter2Step.Value;
        o.Counter2Padding = (int)_numCounter2Pad.Value;

        // ----- Find & replace -----
        o.SwapEnabled = _chkSwap.Checked;
        o.SwapSeparator = _txtSwapSeparator.Text;
        foreach (DataGridViewRow row in _rulesGrid.Rows)
        {
            var find = row.Cells[RuleColFind].Value as string ?? "";
            var replace = row.Cells[RuleColReplace].Value as string ?? "";
            if (find.Length == 0 && replace.Length == 0) continue;
            o.Replacements.Add(new FindReplaceRule
            {
                Find = find,
                Replace = replace,
                UseRegex = row.Cells[RuleColRegex].Value is true,
                CaseSensitive = row.Cells[RuleColCase].Value is true,
                WholeWord = row.Cells[RuleColWord].Value is true,
                FirstOnly = row.Cells[RuleColFirst].Value is true,
                Target = TargetFromCell(row.Cells[RuleColTarget].Value),
                Enabled = row.Cells[RuleColOn].Value is true,
            });
        }

        // ----- Removes -----
        o.RemoveRange = CollectRange(_chkRemoveRange, _numRemoveStart, _numRemoveCount, _chkRemoveFromEnd);
        o.RemoveRange2 = CollectRange(_chkRemoveRange2, _numRemove2Start, _numRemove2Count, _chkRemove2FromEnd);
        o.RemoveBetween = new RemoveBetweenRule
        {
            Enabled = _chkRemoveBetween.Checked,
            From = _txtBetweenFrom.Text,
            To = _txtBetweenTo.Text,
            IncludeDelimiters = _chkBetweenDelims.Checked,
        };
        o.RemoveCameraPrefixes = _chkRemoveCameraPrefixes.Checked;
        o.RemoveDatePatterns = _chkRemoveDatePatterns.Checked;
        o.RemoveGuidPatterns = _chkRemoveGuids.Checked;
        o.RemoveUrls = _chkRemoveUrls.Checked;
        o.RemoveBracketedText = _chkRemoveBrackets.Checked;
        o.RemovePunctuation = _chkRemovePunctuation.Checked;
        o.RemoveNonAscii = _chkRemoveNonAscii.Checked;
        o.RemoveEmoji = _chkRemoveEmoji.Checked;
        o.RemoveNumbers = _chkRemoveNumbers.Checked;
        o.RemoveLeadingNumbers = _chkRemoveLeadingNumbers.Checked;
        o.RemoveTrailingNumbers = _chkRemoveTrailingNumbers.Checked;
        o.RemoveWords = _txtRemoveWords.Text;
        o.StripCharacters = _txtStrip.Text;

        // ----- Inserts -----
        o.Insert = CollectInsert(_chkInsert, _txtInsertText, _cmbInsertAnchor, _numInsertPos, _chkInsertFromEnd, _txtInsertAnchorText);
        o.Insert2 = CollectInsert(_chkInsert2, _txtInsert2Text, _cmbInsert2Anchor, _numInsert2Pos, _chkInsert2FromEnd, _txtInsert2AnchorText);

        // ----- Hygiene -----
        o.TrimWhitespace = _chkTrim.Checked;
        o.CollapseSpaces = _chkCollapse.Checked;
        o.ReplaceUnderscoresWithSpaces = _chkUnderscoresToSpaces.Checked;
        o.ReplaceDotsWithSpaces = _chkDotsToSpaces.Checked;
        o.CollapseRepeatedSeparators = _chkCollapseSeparators.Checked;
        o.TrimSeparators = _chkTrimSeparators.Checked;
        o.ReplaceSpacesWith = _chkReplaceSpaces.Checked ? _txtReplaceSpacesWith.Text : null;
        o.RemoveDiacritics = _chkDiacritics.Checked;
        o.TransliterateToAscii = _chkTransliterate.Checked;
        o.PadNumberRunsTo = (int)_numPadRuns.Value;
        o.MaxNameLength = (int)_numMaxLength.Value;
        o.TruncateFrom = (TruncateFrom)Math.Max(0, _cmbTruncateFrom.SelectedIndex);

        // ----- Case -----
        o.NameCase = CaseFromIndex(_cmbAdvNameCase.SelectedIndex);
        o.ExtensionCase = CaseFromIndex(_cmbAdvExtCase.SelectedIndex);
        o.SmartTitleCase = _chkSmartTitle.Checked;
        o.SmallWords = _txtSmallWords.Text;
        o.PreserveCaseWords = _txtPreserveWords.Text;

        // ----- Affixes -----
        o.Prefix = _txtAdvPrefix.Text;
        o.PrefixOnlyIfMissing = _chkPrefixIfMissing.Checked;
        o.Suffix = _txtAdvSuffix.Text;
        o.SuffixOnlyIfMissing = _chkSuffixIfMissing.Checked;
        o.ParentFolderAsPrefix = _chkParentPrefix.Checked;
        o.ParentPrefixSeparator = _txtParentSep.Text;

        // ----- Extension operations -----
        o.NewExtension = _txtNewExtension.Text.Trim().TrimStart('.');
        o.RemoveExtension = _chkRemoveExtension.Checked;
        o.NormalizeExtensions = _chkNormalizeExt.Checked;
        o.FixExtensionBySniffing = _chkSniffExt.Checked;

        // ----- Scope & filters -----
        o.UseRegexMasks = _chkRegexMasks.Checked;
        o.MinSizeBytes = CollectSizeBytes(_numMinSize, _cmbMinSizeUnit);
        o.MaxSizeBytes = CollectSizeBytes(_numMaxSize, _cmbMaxSizeUnit);
        o.ModifiedAfter = _dtpModifiedAfter.Checked ? _dtpModifiedAfter.Value : null;
        o.ModifiedBefore = _dtpModifiedBefore.Checked ? _dtpModifiedBefore.Value : null;
        o.OnlyWithExif = _chkOnlyWithExif.Checked;
        o.SkipHiddenSystem = _chkSkipHidden.Checked;

        // ----- Safety -----
        o.ConflictPolicy = _radConflictSkip.Checked ? RenameConflictPolicy.Skip
            : _radConflictFail.Checked ? RenameConflictPolicy.Fail
            : RenameConflictPolicy.AppendNumber;
        o.CollisionSuffixFormat = _txtCollisionFormat.Text;
        o.ExportMappingCsv = _chkExportCsv.Checked;
        return o;
    }

    /// <summary>Pushes a full option set (e.g. a preset) into both panels' controls.</summary>
    private void ApplyOptionsToControls(RenameOptions o)
    {
        _suspendPreview = true;

        _txtIncludeMask.Text = o.IncludeMask;
        _txtExcludeMask.Text = o.ExcludeMask;
        // Deliberately NOT applying o.IncludeSubfolders: it controls what Load enumerates,
        // and silently changing the loaded scope from a preset would surprise.

        // ----- Pattern & numbering -----
        _txtAdvPattern.Text = o.Pattern;
        _txtSimplePattern.Text = o.Pattern;
        _cmbDateSource.SelectedIndex = Math.Clamp((int)o.DateSource, 0, _cmbDateSource.Items.Count - 1);
        _numAdvStart.Value = Math.Clamp(o.CounterStart, (int)_numAdvStart.Minimum, (int)_numAdvStart.Maximum);
        _numAdvStep.Value = Math.Clamp(o.CounterStep, (int)_numAdvStep.Minimum, (int)_numAdvStep.Maximum);
        _numAdvPad.Value = Math.Clamp(o.CounterPadding, (int)_numAdvPad.Minimum, (int)_numAdvPad.Maximum);
        _chkCounterPerFolder.Checked = o.CounterPerFolder;
        _cmbCounterStyle.SelectedIndex = Math.Clamp((int)o.CounterStyle, 0, _cmbCounterStyle.Items.Count - 1);
        _cmbNumberingOrder.SelectedIndex = Math.Clamp((int)o.NumberingOrder, 0, _cmbNumberingOrder.Items.Count - 1);
        _numCounter2Start.Value = Math.Clamp(o.Counter2Start, (int)_numCounter2Start.Minimum, (int)_numCounter2Start.Maximum);
        _numCounter2Step.Value = Math.Clamp(o.Counter2Step, (int)_numCounter2Step.Minimum, (int)_numCounter2Step.Maximum);
        _numCounter2Pad.Value = Math.Clamp(o.Counter2Padding, (int)_numCounter2Pad.Minimum, (int)_numCounter2Pad.Maximum);
        _numSimpleStart.Value = _numAdvStart.Value;
        _numSimplePad.Value = _numAdvPad.Value;

        // ----- Find & replace -----
        _chkSwap.Checked = o.SwapEnabled;
        _txtSwapSeparator.Text = o.SwapSeparator;
        _rulesGrid.Rows.Clear();
        foreach (var rule in o.Replacements)
            _rulesGrid.Rows.Add(rule.Enabled, rule.Find, rule.Replace, rule.UseRegex, rule.CaseSensitive,
                rule.WholeWord, rule.FirstOnly, TargetText(rule.Target));
        var first = o.Replacements.FirstOrDefault(r => r.Enabled);
        _txtSimpleFind.Text = first?.Find ?? "";
        _txtSimpleReplace.Text = first?.Replace ?? "";

        // ----- Removes -----
        ApplyRange(o.RemoveRange, _chkRemoveRange, _numRemoveStart, _numRemoveCount, _chkRemoveFromEnd);
        ApplyRange(o.RemoveRange2, _chkRemoveRange2, _numRemove2Start, _numRemove2Count, _chkRemove2FromEnd);
        _chkRemoveBetween.Checked = o.RemoveBetween.Enabled;
        _txtBetweenFrom.Text = o.RemoveBetween.From;
        _txtBetweenTo.Text = o.RemoveBetween.To;
        _chkBetweenDelims.Checked = o.RemoveBetween.IncludeDelimiters;
        _chkRemoveCameraPrefixes.Checked = o.RemoveCameraPrefixes;
        _chkRemoveDatePatterns.Checked = o.RemoveDatePatterns;
        _chkRemoveGuids.Checked = o.RemoveGuidPatterns;
        _chkRemoveUrls.Checked = o.RemoveUrls;
        _chkRemoveBrackets.Checked = o.RemoveBracketedText;
        _chkRemovePunctuation.Checked = o.RemovePunctuation;
        _chkRemoveNonAscii.Checked = o.RemoveNonAscii;
        _chkRemoveEmoji.Checked = o.RemoveEmoji;
        _chkRemoveNumbers.Checked = o.RemoveNumbers;
        _chkRemoveLeadingNumbers.Checked = o.RemoveLeadingNumbers;
        _chkRemoveTrailingNumbers.Checked = o.RemoveTrailingNumbers;
        _txtRemoveWords.Text = o.RemoveWords;
        _txtStrip.Text = o.StripCharacters;

        // ----- Inserts -----
        ApplyInsert(o.Insert, _chkInsert, _txtInsertText, _cmbInsertAnchor, _numInsertPos, _chkInsertFromEnd, _txtInsertAnchorText);
        ApplyInsert(o.Insert2, _chkInsert2, _txtInsert2Text, _cmbInsert2Anchor, _numInsert2Pos, _chkInsert2FromEnd, _txtInsert2AnchorText);

        // ----- Hygiene -----
        _chkTrim.Checked = o.TrimWhitespace;
        _chkCollapse.Checked = o.CollapseSpaces;
        _chkUnderscoresToSpaces.Checked = o.ReplaceUnderscoresWithSpaces;
        _chkDotsToSpaces.Checked = o.ReplaceDotsWithSpaces;
        _chkCollapseSeparators.Checked = o.CollapseRepeatedSeparators;
        _chkTrimSeparators.Checked = o.TrimSeparators;
        _chkReplaceSpaces.Checked = o.ReplaceSpacesWith is not null;
        if (o.ReplaceSpacesWith is not null) _txtReplaceSpacesWith.Text = o.ReplaceSpacesWith;
        _chkDiacritics.Checked = o.RemoveDiacritics;
        _chkTransliterate.Checked = o.TransliterateToAscii;
        _numPadRuns.Value = Math.Clamp(o.PadNumberRunsTo, (int)_numPadRuns.Minimum, (int)_numPadRuns.Maximum);
        _numMaxLength.Value = Math.Clamp(o.MaxNameLength, (int)_numMaxLength.Minimum, (int)_numMaxLength.Maximum);
        _cmbTruncateFrom.SelectedIndex = Math.Clamp((int)o.TruncateFrom, 0, _cmbTruncateFrom.Items.Count - 1);

        // ----- Case -----
        _cmbAdvNameCase.SelectedIndex = Math.Clamp((int)o.NameCase, 0, _cmbAdvNameCase.Items.Count - 1);
        _cmbAdvExtCase.SelectedIndex = Math.Clamp((int)o.ExtensionCase, 0, _cmbAdvExtCase.Items.Count - 1);
        _cmbSimpleNameCase.SelectedIndex = _cmbAdvNameCase.SelectedIndex;
        _cmbSimpleExtCase.SelectedIndex = _cmbAdvExtCase.SelectedIndex;
        _chkSmartTitle.Checked = o.SmartTitleCase;
        _txtSmallWords.Text = o.SmallWords;
        _txtPreserveWords.Text = o.PreserveCaseWords;

        // ----- Affixes -----
        _txtAdvPrefix.Text = o.Prefix;
        _chkPrefixIfMissing.Checked = o.PrefixOnlyIfMissing;
        _txtAdvSuffix.Text = o.Suffix;
        _chkSuffixIfMissing.Checked = o.SuffixOnlyIfMissing;
        _chkParentPrefix.Checked = o.ParentFolderAsPrefix;
        _txtParentSep.Text = o.ParentPrefixSeparator;
        _txtSimplePrefix.Text = o.Prefix;
        _txtSimpleSuffix.Text = o.Suffix;

        // ----- Extension operations -----
        _txtNewExtension.Text = o.NewExtension;
        _chkRemoveExtension.Checked = o.RemoveExtension;
        _chkNormalizeExt.Checked = o.NormalizeExtensions;
        _chkSniffExt.Checked = o.FixExtensionBySniffing;

        // ----- Scope & filters -----
        _chkRegexMasks.Checked = o.UseRegexMasks;
        ApplySizeControls(o.MinSizeBytes, _numMinSize, _cmbMinSizeUnit);
        ApplySizeControls(o.MaxSizeBytes, _numMaxSize, _cmbMaxSizeUnit);
        ApplyNullableDate(_dtpModifiedAfter, o.ModifiedAfter);
        ApplyNullableDate(_dtpModifiedBefore, o.ModifiedBefore);
        _chkOnlyWithExif.Checked = o.OnlyWithExif;
        _chkSkipHidden.Checked = o.SkipHiddenSystem;

        // ----- Safety -----
        _radConflictAppend.Checked = o.ConflictPolicy == RenameConflictPolicy.AppendNumber;
        _radConflictSkip.Checked = o.ConflictPolicy == RenameConflictPolicy.Skip;
        _radConflictFail.Checked = o.ConflictPolicy == RenameConflictPolicy.Fail;
        _txtCollisionFormat.Text = o.CollisionSuffixFormat;
        _chkExportCsv.Checked = o.ExportMappingCsv;

        _suspendPreview = false;
        UpdateAdvancedInterlocks();
        UpdateCollisionExample();
        ValidateRules();
        RequestPreview();
    }

    // ---------- small mapping helpers ----------

    private static CaseTransform CaseFromIndex(int index) =>
        index is >= 0 and <= (int)CaseTransform.RandomCase ? (CaseTransform)index : CaseTransform.None;

    private static string TargetText(ReplaceTarget target) => target switch
    {
        ReplaceTarget.ExtensionOnly => "Ext",
        ReplaceTarget.Both => "Both",
        _ => "Name",
    };

    private static ReplaceTarget TargetFromCell(object? value) => (value as string) switch
    {
        "Ext" => ReplaceTarget.ExtensionOnly,
        "Both" => ReplaceTarget.Both,
        _ => ReplaceTarget.NameOnly,
    };

    private static RemoveRangeRule CollectRange(CheckBox chk, NumericUpDown start, NumericUpDown count, CheckBox fromEnd) => new()
    {
        Enabled = chk.Checked,
        Start = (int)start.Value,
        Count = (int)count.Value,
        FromEnd = fromEnd.Checked,
    };

    private static void ApplyRange(RemoveRangeRule rule, CheckBox chk, NumericUpDown start, NumericUpDown count, CheckBox fromEnd)
    {
        chk.Checked = rule.Enabled;
        start.Value = Math.Clamp(rule.Start, (int)start.Minimum, (int)start.Maximum);
        count.Value = Math.Clamp(rule.Count, (int)count.Minimum, (int)count.Maximum);
        fromEnd.Checked = rule.FromEnd;
    }

    private static InsertRule CollectInsert(CheckBox chk, TextBox txt, ComboBox anchor,
        NumericUpDown pos, CheckBox fromEnd, TextBox anchorText) => new()
    {
        Enabled = chk.Checked,
        Text = txt.Text,
        Anchor = (InsertAnchor)Math.Max(0, anchor.SelectedIndex),
        Position = (int)pos.Value,
        FromEnd = fromEnd.Checked,
        AnchorText = anchorText.Text,
    };

    private static void ApplyInsert(InsertRule rule, CheckBox chk, TextBox txt, ComboBox anchor,
        NumericUpDown pos, CheckBox fromEnd, TextBox anchorText)
    {
        chk.Checked = rule.Enabled;
        txt.Text = rule.Text;
        anchor.SelectedIndex = Math.Clamp((int)rule.Anchor, 0, anchor.Items.Count - 1);
        pos.Value = Math.Clamp(rule.Position, (int)pos.Minimum, (int)pos.Maximum);
        fromEnd.Checked = rule.FromEnd;
        anchorText.Text = rule.AnchorText;
    }

    private static long CollectSizeBytes(NumericUpDown num, ComboBox unit) =>
        (long)num.Value * SizeUnitMultipliers[Math.Max(0, unit.SelectedIndex)];

    private static void ApplySizeControls(long bytes, NumericUpDown num, ComboBox unit)
    {
        int index = 2; // 0 reads most naturally as "0 MB"
        if (bytes > 0)
            for (index = SizeUnitMultipliers.Length - 1; index > 0 && bytes % SizeUnitMultipliers[index] != 0; index--) { }
        unit.SelectedIndex = index;
        num.Value = Math.Clamp(bytes / SizeUnitMultipliers[index], (long)num.Minimum, (long)num.Maximum);
    }

    private static void ApplyNullableDate(DateTimePicker picker, DateTime? value)
    {
        if (value is DateTime v)
        {
            picker.Value = v < picker.MinDate ? picker.MinDate : v > picker.MaxDate ? picker.MaxDate : v;
            picker.Checked = true;
        }
        else
        {
            picker.Checked = false;
        }
    }
}
