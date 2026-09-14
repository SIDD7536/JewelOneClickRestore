namespace JewelOneClickRestore;

public sealed class BakSelectionForm : Form
{
    private readonly ListBox _list = new();
    private readonly Button _okButton = new();
    private readonly Button _cancelButton = new();

    public string? SelectedBakPath => _list.SelectedItem as string;

    public BakSelectionForm(IReadOnlyList<string> bakFiles)
    {
        Text = "Select Backup File";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Width = 720;
        Height = 420;
        Font = new Font("Segoe UI", 9F);

        var title = new Label
        {
            Text = "Multiple .BAK files were found inside the ZIP.\r\nSelect the backup you want to restore:",
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 58,
            Padding = new Padding(12, 12, 12, 4)
        };

        _list.Dock = DockStyle.Fill;
        _list.HorizontalScrollbar = true;
        _list.IntegralHeight = false;
        foreach (var file in bakFiles)
            _list.Items.Add(file);

        if (_list.Items.Count > 0)
            _list.SelectedIndex = 0;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };

        _okButton.Text = "Use Selected Backup";
        _okButton.Width = 160;
        _okButton.Height = 32;
        _okButton.DialogResult = DialogResult.OK;

        _cancelButton.Text = "Cancel";
        _cancelButton.Width = 90;
        _cancelButton.Height = 32;
        _cancelButton.DialogResult = DialogResult.Cancel;

        buttons.Controls.Add(_okButton);
        buttons.Controls.Add(_cancelButton);

        Controls.Add(_list);
        Controls.Add(title);
        Controls.Add(buttons);

        AcceptButton = _okButton;
        CancelButton = _cancelButton;
    }
}
