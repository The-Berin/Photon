using Photon.Core.Models;

namespace Photon.App.Forms;

public partial class BatchRenameForm
{
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
            _rulesGrid.Rows.Add(true, _txtSimpleFind.Text, _txtSimpleReplace.Text, false, false);
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

        o.Pattern = string.IsNullOrWhiteSpace(_txtAdvPattern.Text) ? "{name}" : _txtAdvPattern.Text;
        o.DateSource = (DateSource)Math.Max(0, _cmbDateSource.SelectedIndex);

        o.CounterStart = (int)_numAdvStart.Value;
        o.CounterStep = (int)_numAdvStep.Value;
        o.CounterPadding = (int)_numAdvPad.Value;
        o.CounterPerFolder = _chkCounterPerFolder.Checked;

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
                Enabled = row.Cells[RuleColOn].Value is true,
            });
        }

        o.RemoveRange = new RemoveRangeRule
        {
            Enabled = _chkRemoveRange.Checked,
            Start = (int)_numRemoveStart.Value,
            Count = (int)_numRemoveCount.Value,
            FromEnd = _chkRemoveFromEnd.Checked,
        };
        o.RemoveNumbers = _chkRemoveNumbers.Checked;
        o.RemoveBracketedText = _chkRemoveBrackets.Checked;

        o.Insert = new InsertRule
        {
            Enabled = _chkInsert.Checked,
            Text = _txtInsertText.Text,
            Position = (int)_numInsertPos.Value,
            FromEnd = _chkInsertFromEnd.Checked,
        };

        o.TrimWhitespace = _chkTrim.Checked;
        o.CollapseSpaces = _chkCollapse.Checked;
        o.RemoveDiacritics = _chkDiacritics.Checked;
        o.StripCharacters = _txtStrip.Text;
        o.ReplaceSpacesWith = _chkReplaceSpaces.Checked ? _txtReplaceSpacesWith.Text : null;

        o.NameCase = CaseFromIndex(_cmbAdvNameCase.SelectedIndex);
        o.ExtensionCase = CaseFromIndex(_cmbAdvExtCase.SelectedIndex);
        o.Prefix = _txtAdvPrefix.Text;
        o.Suffix = _txtAdvSuffix.Text;

        o.ConflictPolicy = _radConflictSkip.Checked ? RenameConflictPolicy.Skip
            : _radConflictFail.Checked ? RenameConflictPolicy.Fail
            : RenameConflictPolicy.AppendNumber;
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

        _txtAdvPattern.Text = o.Pattern;
        _txtSimplePattern.Text = o.Pattern;
        _cmbDateSource.SelectedIndex = Math.Clamp((int)o.DateSource, 0, _cmbDateSource.Items.Count - 1);

        _numAdvStart.Value = Math.Clamp(o.CounterStart, (int)_numAdvStart.Minimum, (int)_numAdvStart.Maximum);
        _numAdvStep.Value = Math.Clamp(o.CounterStep, (int)_numAdvStep.Minimum, (int)_numAdvStep.Maximum);
        _numAdvPad.Value = Math.Clamp(o.CounterPadding, (int)_numAdvPad.Minimum, (int)_numAdvPad.Maximum);
        _chkCounterPerFolder.Checked = o.CounterPerFolder;
        _numSimpleStart.Value = _numAdvStart.Value;
        _numSimplePad.Value = _numAdvPad.Value;

        _rulesGrid.Rows.Clear();
        foreach (var rule in o.Replacements)
            _rulesGrid.Rows.Add(rule.Enabled, rule.Find, rule.Replace, rule.UseRegex, rule.CaseSensitive);
        var first = o.Replacements.FirstOrDefault(r => r.Enabled);
        _txtSimpleFind.Text = first?.Find ?? "";
        _txtSimpleReplace.Text = first?.Replace ?? "";

        _chkRemoveRange.Checked = o.RemoveRange.Enabled;
        _numRemoveStart.Value = Math.Clamp(o.RemoveRange.Start, (int)_numRemoveStart.Minimum, (int)_numRemoveStart.Maximum);
        _numRemoveCount.Value = Math.Clamp(o.RemoveRange.Count, (int)_numRemoveCount.Minimum, (int)_numRemoveCount.Maximum);
        _chkRemoveFromEnd.Checked = o.RemoveRange.FromEnd;
        _chkRemoveNumbers.Checked = o.RemoveNumbers;
        _chkRemoveBrackets.Checked = o.RemoveBracketedText;

        _chkInsert.Checked = o.Insert.Enabled;
        _txtInsertText.Text = o.Insert.Text;
        _numInsertPos.Value = Math.Clamp(o.Insert.Position, (int)_numInsertPos.Minimum, (int)_numInsertPos.Maximum);
        _chkInsertFromEnd.Checked = o.Insert.FromEnd;

        _chkTrim.Checked = o.TrimWhitespace;
        _chkCollapse.Checked = o.CollapseSpaces;
        _chkDiacritics.Checked = o.RemoveDiacritics;
        _txtStrip.Text = o.StripCharacters;
        _chkReplaceSpaces.Checked = o.ReplaceSpacesWith is not null;
        if (o.ReplaceSpacesWith is not null) _txtReplaceSpacesWith.Text = o.ReplaceSpacesWith;

        _cmbAdvNameCase.SelectedIndex = Math.Clamp((int)o.NameCase, 0, _cmbAdvNameCase.Items.Count - 1);
        _cmbAdvExtCase.SelectedIndex = Math.Clamp((int)o.ExtensionCase, 0, _cmbAdvExtCase.Items.Count - 1);
        _cmbSimpleNameCase.SelectedIndex = _cmbAdvNameCase.SelectedIndex;
        _cmbSimpleExtCase.SelectedIndex = _cmbAdvExtCase.SelectedIndex;

        _txtAdvPrefix.Text = o.Prefix;
        _txtAdvSuffix.Text = o.Suffix;
        _txtSimplePrefix.Text = o.Prefix;
        _txtSimpleSuffix.Text = o.Suffix;

        _radConflictAppend.Checked = o.ConflictPolicy == RenameConflictPolicy.AppendNumber;
        _radConflictSkip.Checked = o.ConflictPolicy == RenameConflictPolicy.Skip;
        _radConflictFail.Checked = o.ConflictPolicy == RenameConflictPolicy.Fail;

        _suspendPreview = false;
        ValidateRules();
        RequestPreview();
    }

    private static CaseTransform CaseFromIndex(int index) =>
        index is >= 0 and <= (int)CaseTransform.InvertCase ? (CaseTransform)index : CaseTransform.None;
}
