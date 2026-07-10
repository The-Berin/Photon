namespace Photon.App.Forms;

public partial class BatchRenameForm
{
    private TextBox _txtSimplePattern = null!;
    private TextBox _txtSimpleFind = null!;
    private TextBox _txtSimpleReplace = null!;
    private TextBox _txtSimplePrefix = null!;
    private TextBox _txtSimpleSuffix = null!;
    private ComboBox _cmbSimpleNameCase = null!;
    private ComboBox _cmbSimpleExtCase = null!;
    private NumericUpDown _numSimpleStart = null!;
    private NumericUpDown _numSimplePad = null!;

    private const int GroupWidth = 448;

    private void BuildSimplePanel()
    {
        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(4),
        };

        // Pattern
        var gbPattern = NewGroup("New name pattern", 104);
        _txtSimplePattern = new TextBox { Location = new Point(10, 24), Width = 320, Text = "{name}" };
        var btnToken = new Button { Text = "Insert token ▾", Location = new Point(336, 22), Width = 100 };
        var tokenMenu = TokenCatalog.BuildInsertMenu(_txtSimplePattern);
        btnToken.Click += (_, _) => tokenMenu.Show(btnToken, new Point(0, btnToken.Height));
        var hint = new Label
        {
            Text = "{name} keeps the original name. Try IMG_{yyyy}-{MM}-{dd}_{counter}",
            Location = new Point(10, 52),
            Size = new Size(426, 40),
            ForeColor = SystemColors.GrayText,
        };
        gbPattern.Controls.AddRange([_txtSimplePattern, btnToken, hint]);

        // Find & replace
        var gbFind = NewGroup("Find && replace", 84);
        gbFind.Controls.Add(NewInlineLabel("Find:", 10, 27));
        _txtSimpleFind = new TextBox { Location = new Point(52, 24), Width = 160 };
        gbFind.Controls.Add(NewInlineLabel("Replace with:", 222, 27));
        _txtSimpleReplace = new TextBox { Location = new Point(302, 24), Width = 134 };
        var findHint = new Label
        {
            Text = "Plain text, applied once per occurrence. Regex lives in Advanced mode.",
            Location = new Point(10, 52),
            Size = new Size(426, 18),
            ForeColor = SystemColors.GrayText,
        };
        gbFind.Controls.AddRange([_txtSimpleFind, _txtSimpleReplace, findHint]);

        // Prefix / suffix
        var gbAffix = NewGroup("Add text", 60);
        gbAffix.Controls.Add(NewInlineLabel("Prefix:", 10, 27));
        _txtSimplePrefix = new TextBox { Location = new Point(56, 24), Width = 156 };
        gbAffix.Controls.Add(NewInlineLabel("Suffix:", 226, 27));
        _txtSimpleSuffix = new TextBox { Location = new Point(272, 24), Width = 164 };
        gbAffix.Controls.AddRange([_txtSimplePrefix, _txtSimpleSuffix]);

        // Case
        var gbCase = NewGroup("Case", 60);
        gbCase.Controls.Add(NewInlineLabel("Name:", 10, 27));
        _cmbSimpleNameCase = NewCaseCombo(56, 24, 156);
        gbCase.Controls.Add(NewInlineLabel("Extension:", 226, 27));
        _cmbSimpleExtCase = NewCaseCombo(292, 24, 144);
        gbCase.Controls.AddRange([_cmbSimpleNameCase, _cmbSimpleExtCase]);

        // Counter
        var gbCounter = NewGroup("Counter — used when the pattern contains {counter}", 60);
        gbCounter.Controls.Add(NewInlineLabel("Start at:", 10, 27));
        _numSimpleStart = new NumericUpDown
        {
            Location = new Point(64, 24), Width = 80, Minimum = 0, Maximum = 1_000_000_000, Value = 1,
        };
        gbCounter.Controls.Add(NewInlineLabel("Pad to digits:", 226, 27));
        _numSimplePad = new NumericUpDown
        {
            Location = new Point(306, 24), Width = 60, Minimum = 0, Maximum = 12, Value = 3,
        };
        gbCounter.Controls.AddRange([_numSimpleStart, _numSimplePad]);

        stack.Controls.AddRange([gbPattern, gbFind, gbAffix, gbCase, gbCounter]);
        _simpleHost.Controls.Add(stack);

        WireChange(_txtSimplePattern, _txtSimpleFind, _txtSimpleReplace, _txtSimplePrefix, _txtSimpleSuffix,
            _cmbSimpleNameCase, _cmbSimpleExtCase, _numSimpleStart, _numSimplePad);
    }

    private static GroupBox NewGroup(string title, int height) => new()
    {
        Text = title,
        Width = GroupWidth,
        Height = height,
        Margin = new Padding(3, 3, 3, 6),
    };

    private static Label NewInlineLabel(string text, int x, int y) => new()
    {
        Text = text,
        AutoSize = true,
        Location = new Point(x, y),
    };

    private static ComboBox NewCaseCombo(int x, int y, int width)
    {
        // Item order mirrors the CaseTransform enum so SelectedIndex maps directly.
        var cmb = new ComboBox
        {
            Location = new Point(x, y),
            Width = width,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        cmb.Items.AddRange(["Keep as-is", "lowercase", "UPPERCASE", "Title Case", "Sentence case", "iNVERT cASE",
            "PascalCase", "camelCase", "snake_case", "kebab-case", "rAnDoM cAsE"]);
        cmb.SelectedIndex = 0;
        return cmb;
    }
}
