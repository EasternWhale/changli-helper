using System.Drawing.Drawing2D;

internal sealed class ChatWindow : Form
{
    private static readonly Color Ink = Color.FromArgb(50, 34, 25);
    private static readonly Color Muted = Color.FromArgb(108, 87, 74);
    private static readonly Color Paper = Color.White;
    private static readonly Color Line = Color.FromArgb(234, 219, 206);
    private static readonly Color Accent = Color.FromArgb(172, 75, 39);
    private static readonly Color Rose = Color.FromArgb(255, 241, 231);
    private static readonly (string Label, string Prompt)[] Prompts =
    [
        ("助手能力", "你能帮我做什么？"),
        ("短视频文案", "帮我给蜀绣笔记本写短视频文案。"),
        ("非遗产品推荐", "推荐适合酒店大堂的非遗装饰产品。"),
        ("目标客群分析", "手工竹编收纳筐适合卖给谁？"),
        ("品牌故事", "帮我写一个苏绣品牌的店铺故事。")
    ];
    private static readonly string[] TaskSummaries =
    [
        "功能与用法", "脚本与画面", "按场景选品", "人群与渠道", "工艺与叙事"
    ];
    private static readonly (string Instruction, string GoalLabel, string GoalExample)[] TaskDetails =
    [
        ("介绍你能帮我完成的任务，并给出几个可直接开始的例子。", "想了解的方向", "例如：非遗文创推广"),
        ("为这个产品写一段短视频文案，给出开场、画面建议和结尾。", "平台或风格", "例如：小红书，温暖自然"),
        ("按使用场景推荐合适的非遗产品，并说明搭配理由。", "使用场景或预算", "例如：酒店大堂，预算 3000 元"),
        ("分析产品的目标客群、购买动机和适合的销售渠道。", "价格或销售渠道", "例如：定价 129 元，线上销售"),
        ("写一段可用于店铺首页的品牌故事，突出工艺和特色。", "品牌特色或语气", "例如：苏绣手作，朴实温暖")
    ];
    private readonly AgentApi api = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly string userId = Guid.NewGuid().ToString();
    private readonly FlowLayoutPanel messages = new();
    private readonly TextBox composer = new();
    private readonly Label status = new();
    private readonly Button send = new();
    private readonly Button reset = new();
    private readonly List<Button> promptButtons = new();
    private readonly List<Button> taskButtons = new();
    private readonly Panel host;
    private readonly TableLayoutPanel layout;
    private FlowLayoutPanel? suggestions;
    private TableLayoutPanel? promptArea;
    private TableLayoutPanel? taskPanel;
    private readonly Label goalLabel = new();
    private readonly TextBox product = new();
    private readonly TextBox goal = new();
    private int taskHeight;
    private int? selectedTask;
    private int welcomeHeight;
    private string? conversationId;

    public ChatWindow()
    {
        Text = "长离助手";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Font = new Font("Microsoft YaHei UI", 11f);
        ForeColor = Ink;
        BackColor = Paper;
        ClientSize = new Size(960, 730);
        MinimumSize = new Size(620, 600);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;

        host = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(253, 247, 240) };
        layout = BuildLayout();
        host.Controls.Add(layout);
        Controls.Add(host);
        host.SizeChanged += (_, _) => CenterLayout();
        DpiChanged += (_, _) => CenterLayout();
        CenterLayout();
        reset.Click += (_, _) => NewConversation();
        send.Click += async (_, _) => await Send();
        composer.KeyDown += async (_, e) =>
        {
            if (!e.Control || e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            await Send();
        };
        FormClosing += (_, _) => lifetime.Cancel();
        Shown += (_, _) => composer.Focus();
        NewConversation();
    }

    private void CenterLayout()
    {
        bool welcoming = welcomeHeight > 0;
        int maxWidth = (welcoming ? 900 : 1090) * DeviceDpi / 96;
        int width = Math.Min(host.ClientSize.Width, maxWidth);
        int height = host.ClientSize.Height;
        if (welcoming)
        {
            int controlsHeight = layout.RowStyles.Cast<RowStyle>()
                .Where(row => row.SizeType == SizeType.Absolute)
                .Sum(row => (int)Math.Ceiling(row.Height));
            int greetingHeight = selectedTask is null ? welcomeHeight : 0;
            height = Math.Min(height, controlsHeight + layout.Padding.Vertical + greetingHeight);
        }
        layout.SetBounds((host.ClientSize.Width - width) / 2,
            (host.ClientSize.Height - height) / 2, width, height);
    }

    private TableLayoutPanel BuildLayout()
    {
        var layout = new TableLayoutPanel
        {
            BackColor = Paper, Padding = new Padding(24, 20, 24, 14),
            ColumnCount = 1, RowCount = 6
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Control header = BuildHeader();
        Control input = BuildComposer();
        var tools = BuildPromptStrip(shortLabels: true);
        suggestions = BuildPromptStrip(shortLabels: false);
        taskPanel = BuildTaskPanel();
        promptArea = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 1, RowCount = 1
        };
        promptArea.Controls.Add(suggestions, 0, 0);
        status.Font = new Font(Font.FontFamily, 9f);
        int footerHeight = TextRenderer.MeasureText("快捷键", status.Font).Height + 8;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, header.Height + header.Margin.Vertical + 14));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, StripHeight(tools)));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, StripHeight(suggestions)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, input.Height));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, footerHeight));
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(tools, 0, 1);

        messages.Dock = DockStyle.Fill;
        messages.Margin = new Padding(0, 2, 0, 6);
        messages.FlowDirection = FlowDirection.TopDown;
        messages.WrapContents = false;
        messages.AutoScroll = true;
        messages.AccessibleName = "聊天记录";
        messages.SizeChanged += (_, _) => LayoutMessages();
        layout.Controls.Add(messages, 0, 2);
        layout.Controls.Add(promptArea, 0, 3);

        layout.Controls.Add(input, 0, 4);
        status.TextAlign = ContentAlignment.MiddleLeft;
        status.ForeColor = Muted;
        status.Dock = DockStyle.Fill;
        status.AccessibleName = "连接状态";
        layout.Controls.Add(status, 0, 5);
        return layout;
    }

    private TableLayoutPanel BuildTaskPanel()
    {
        var panel = new TableLayoutPanel
        {
            BackColor = Color.White, Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 8),
            Margin = new Padding(0, 0, 0, 8), ColumnCount = 2, RowCount = 3
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var productLabel = CreateLabel("产品、品牌或品类", 9f, Ink, true);
        goalLabel.Font = productLabel.Font;
        goalLabel.AutoSize = true;
        goalLabel.Margin = Padding.Empty;
        goalLabel.ForeColor = Ink;
        product.Font = goal.Font = new Font(Font.FontFamily, 9f);
        product.PlaceholderText = "例如：蜀绣笔记本";
        product.AccessibleName = productLabel.Text;
        product.MaxLength = goal.MaxLength = 400;
        product.Dock = goal.Dock = DockStyle.Top;
        product.Margin = goal.Margin = new Padding(0, 2, 8, 3);
        panel.Controls.Add(productLabel, 0, 0);
        panel.Controls.Add(goalLabel, 1, 0);
        panel.Controls.Add(product, 0, 1);
        panel.Controls.Add(goal, 1, 1);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false
        };
        var draft = new Button();
        var close = new Button();
        StyleButton(draft, "填入提问", Accent, Color.White);
        StyleButton(close, "收起", Color.White, Muted);
        draft.Font = close.Font = product.Font;
        draft.Size = ButtonSize(draft, 92);
        close.Size = ButtonSize(close, 66);
        close.Margin = new Padding(8, 0, 0, 0);
        draft.Click += (_, _) => FillTaskDraft();
        close.Click += (_, _) => SelectTask(null);
        actions.Controls.Add(draft);
        actions.Controls.Add(close);
        panel.Controls.Add(actions, 0, 2);
        panel.SetColumnSpan(actions, 2);
        int labelHeight = productLabel.PreferredHeight + 4;
        int fieldHeight = product.PreferredHeight + product.Margin.Vertical + 6;
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, labelHeight));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, fieldHeight));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, draft.Height + 2));
        taskHeight = labelHeight + fieldHeight + draft.Height + 2 + panel.Padding.Vertical + panel.Margin.Vertical;
        return panel;
    }

    private FlowLayoutPanel BuildPromptStrip(bool shortLabels)
    {
        var strip = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, Margin = Padding.Empty, AutoScroll = true, WrapContents = false,
            Padding = new Padding(0, 0, 0, 4),
            AccessibleName = shortLabels ? "功能导航" : "快捷提问"
        };
        for (int index = 0; index < Prompts.Length; index++)
        {
            var (label, prompt) = Prompts[index];
            int task = index;
            var button = new Button();
            string caption = shortLabels ? label + Environment.NewLine + TaskSummaries[index] : prompt.TrimEnd('。');
            StyleButton(button, caption, Color.White, shortLabels ? Ink : Muted);
            button.Font = new Font(Font.FontFamily, 8.5f);
            button.Size = ButtonSize(button, 65);
            button.Margin = new Padding(0, 0, 7, 6);
            button.AccessibleDescription = prompt;
            if (shortLabels)
            {
                button.TextAlign = ContentAlignment.MiddleLeft;
                button.Padding = new Padding(9, 0, 6, 0);
                button.Size = new Size(button.Width + 15, button.Height + 10);
                button.AccessibleName = label;
                taskButtons.Add(button);
                button.Click += (_, _) => SelectTask(selectedTask == task ? null : task);
            }
            else button.Click += async (_, _) => await Send(prompt);
            promptButtons.Add(button);
            strip.Controls.Add(button);
        }
        return strip;
    }

    private static int StripHeight(FlowLayoutPanel strip) =>
        strip.Padding.Vertical + SystemInformation.HorizontalScrollBarHeight +
        strip.Controls.Cast<Control>().Max(button => button.Height + button.Margin.Vertical);

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 13),
            ColumnCount = 3, RowCount = 1
        };
        var mark = CreateLabel("长", 18f, Color.White, true);
        int emblemSize = Math.Max(52, mark.PreferredHeight + 6);
        var emblem = new Surface(Accent, null) { Size = new Size(emblemSize, emblemSize), Anchor = AnchorStyles.Left, Margin = Padding.Empty };
        mark.AutoSize = false;
        mark.Dock = DockStyle.Fill;
        mark.TextAlign = ContentAlignment.MiddleCenter;
        emblem.Controls.Add(mark);
        header.Controls.Add(emblem, 0, 0);

        var titles = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Margin = Padding.Empty, Padding = new Padding(0, 4, 0, 0)
        };
        var title = CreateLabel("长离助手", 19f, Ink, true);
        var subtitle = CreateLabel("功能导航与对话", 9f, Muted);
        titles.Controls.Add(title);
        titles.Controls.Add(subtitle);
        header.Controls.Add(titles, 1, 0);

        StyleButton(reset, "＋ 新对话", Color.White, Accent);
        reset.Size = ButtonSize(reset, 111);
        reset.Anchor = AnchorStyles.Right;
        header.Controls.Add(reset, 2, 0);
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, emblemSize + 15));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, reset.Width + 2));
        header.Height = Math.Max(emblemSize, title.PreferredHeight + subtitle.PreferredHeight + 7);
        return header;
    }

    private Control BuildComposer()
    {
        var surface = new Surface(Color.White, Line) { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(15, 12, 12, 12) };
        var entry = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2,
            Margin = Padding.Empty, BackColor = Color.White
        };
        var caption = CreateLabel("消息", 9f, Muted, true);
        entry.RowStyles.Add(new RowStyle(SizeType.Absolute, caption.PreferredHeight + 5));
        entry.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        entry.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        entry.Controls.Add(caption, 0, 0);
        composer.Dock = DockStyle.Fill;
        composer.Margin = new Padding(0, 2, 12, 0);
        composer.BorderStyle = BorderStyle.None;
        composer.BackColor = Color.White;
        composer.ForeColor = Ink;
        composer.Multiline = true;
        composer.ScrollBars = ScrollBars.Vertical;
        composer.MaxLength = 4000;
        composer.AccessibleName = "输入消息";
        composer.PlaceholderText = "问问长离助手，例如帮我写一段短视频文案";
        entry.Controls.Add(composer, 0, 1);
        StyleButton(send, "发送  ↑", Accent, Color.White);
        send.Size = ButtonSize(send, 105);
        entry.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, send.Width + 3));
        send.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        entry.Controls.Add(send, 1, 1);
        surface.Controls.Add(entry);
        surface.Height = Math.Max(118, caption.PreferredHeight + send.Height + surface.Padding.Vertical + 14);
        return surface;
    }

    private Label CreateLabel(string text, float size, Color color, bool bold = false) => new()
    {
        Text = text, AutoSize = true, Margin = Padding.Empty, BackColor = Color.Transparent,
        ForeColor = color, UseMnemonic = false,
        Font = new Font(Font.FontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular)
    };

    private void StyleButton(Button button, string text, Color background, Color foreground)
    {
        button.Text = text;
        button.Font = Font;
        button.Margin = Padding.Empty;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = background == Color.White ? Line : background;
        button.BackColor = background;
        button.ForeColor = foreground;
        button.Cursor = Cursors.Hand;
    }

    private static Size ButtonSize(Button button, int minimumWidth)
    {
        Size text = TextRenderer.MeasureText(button.Text, button.Font);
        return new Size(Math.Max(minimumWidth, text.Width + 20), Math.Max(40, text.Height + 12));
    }

    private void NewConversation()
    {
        conversationId = null;
        selectedTask = null;
        product.Clear();
        goal.Clear();
        while (messages.Controls.Count > 0) messages.Controls[0].Dispose();
        var welcome = new Surface(Color.White, Line)
        {
            Margin = new Padding(0, 0, 0, 14)
        };
        var title = CreateLabel("今天想做什么？", 21f, Ink, true);
        var subtitle = CreateLabel("选择任务，填写条件，再把提问发给长离助手。", 10f, Muted);
        title.BackColor = subtitle.BackColor = Color.White;
        title.Location = new Point(21, 17);
        subtitle.Location = new Point(23, title.Top + title.PreferredHeight + 7);
        welcome.Height = Math.Max(124, subtitle.Top + subtitle.PreferredHeight + 16);
        welcome.Controls.AddRange(new Control[] { title, subtitle });
        messages.Controls.Add(welcome);
        welcomeHeight = welcome.Height + welcome.Margin.Vertical + messages.Margin.Vertical + 4;
        SelectTask(null);
        LayoutMessages();
        status.Text = "Ctrl + Enter 发送    ·    Enter 换行";
        composer.Focus();
    }

    private void LayoutMessages()
    {
        int available = Math.Max(300, messages.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2);
        messages.SuspendLayout();
        foreach (Control item in messages.Controls)
        {
            item.Width = available;
            if (item is MessageRow row) row.Fit(available);
        }
        messages.ResumeLayout();
    }

    private void Append(string author, string text, bool outgoing = false, bool error = false)
    {
        if (messages.Controls.Count == 1 && messages.Controls[0] is Surface welcome)
        {
            welcome.Dispose();
            welcomeHeight = 0;
            SelectTask(null);
        }
        var heading = CreateLabel(author, 9f, Accent, true);
        var body = CreateLabel(text, 11f, Ink);
        var row = new MessageRow(heading, body, outgoing, error);
        var menu = new ContextMenuStrip();
        menu.Items.Add("复制消息", null, (_, _) => Clipboard.SetText(text));
        row.ContextMenuStrip = heading.ContextMenuStrip = body.ContextMenuStrip = menu;
        row.Disposed += (_, _) => menu.Dispose();
        messages.Controls.Add(row);
        LayoutMessages();
        messages.ScrollControlIntoView(row);
    }

    private void SelectTask(int? task)
    {
        selectedTask = task;
        for (int index = 0; index < taskButtons.Count; index++)
        {
            bool active = task == index;
            taskButtons[index].BackColor = active ? Rose : Color.White;
            taskButtons[index].FlatAppearance.BorderColor = active ? Accent : Line;
        }
        if (promptArea is null || taskPanel is null || suggestions is null) return;
        messages.Visible = task is null || welcomeHeight == 0;
        promptArea.Controls.Clear();
        if (task is int indexOfTask)
        {
            var details = TaskDetails[indexOfTask];
            goalLabel.Text = goal.AccessibleName = details.GoalLabel;
            goal.PlaceholderText = details.GoalExample;
            goal.Clear();
            promptArea.Controls.Add(taskPanel, 0, 0);
            layout.RowStyles[3].Height = taskHeight;
        }
        else if (welcomeHeight > 0)
        {
            promptArea.Controls.Add(suggestions, 0, 0);
            layout.RowStyles[3].Height = StripHeight(suggestions);
        }
        else
        {
            layout.RowStyles[3].Height = 0;
        }
        CenterLayout();
        if (task is not null) product.Focus();
    }

    private void FillTaskDraft()
    {
        if (selectedTask is not int task) return;
        var details = TaskDetails[task];
        var parts = new List<string> { details.Instruction };
        if (!string.IsNullOrWhiteSpace(product.Text)) parts.Add("产品、品牌或品类：" + product.Text.Trim());
        if (!string.IsNullOrWhiteSpace(goal.Text)) parts.Add(details.GoalLabel + "：" + goal.Text.Trim());
        if (composer.TextLength > 0) parts.Add("补充说明：" + composer.Text.Trim());
        string draft = string.Join(Environment.NewLine, parts);
        if (draft.Length > composer.MaxLength)
        {
            status.Text = "提问超过 4000 字，请先缩短输入框中的草稿。";
            return;
        }
        composer.Text = draft;
        SelectTask(null);
        composer.Focus();
        composer.SelectionStart = composer.TextLength;
    }

    private async Task Send(string? preset = null)
    {
        if (!send.Enabled) return;
        string message = preset ?? composer.Text.Trim();
        if (message.Length == 0) return;
        if (preset is null) composer.Clear();
        if (selectedTask is not null) SelectTask(null);
        Append("你", message, outgoing: true);
        send.Enabled = reset.Enabled = false;
        foreach (var button in promptButtons) button.Enabled = false;
        status.Text = "正在等待长离助手回复…";
        try
        {
            Reply reply = await api.SendAsync(message, userId, conversationId, lifetime.Token);
            if (IsDisposed) return;
            conversationId = reply.ConversationId;
            Append("长离助手", reply.Answer);
            status.Text = "已收到回复    ·    Ctrl + Enter 发送";
        }
        catch (Exception error)
        {
            if (IsDisposed || lifetime.IsCancellationRequested) return;
            string detail = error switch
            {
                OperationCanceledException => "请求超时，请重试。",
                HttpRequestException => api.UsesPublicEndpoint
                    ? "连不上长离助手，请检查网络连接，或联系分享人确认服务已启动。"
                    : "连不上长离助手。此版本使用局域网，请确认设备与部署电脑在同一网络。",
                InvalidOperationException => error.Message,
                _ => "无法读取回复，请重试。"
            };
            Append("请求失败", detail, error: true);
            if (composer.TextLength == 0) composer.Text = message;
            status.Text = "发送失败    ·    检查连接后重试";
        }
        finally
        {
            if (!IsDisposed)
            {
                send.Enabled = reset.Enabled = true;
                foreach (var button in promptButtons) button.Enabled = true;
                composer.Focus();
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            api.Dispose();
            if (taskPanel?.Parent is null) taskPanel?.Dispose();
            if (suggestions?.Parent is null) suggestions?.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class MessageRow : Panel
    {
        private readonly Surface bubble;
        private readonly Label heading;
        private readonly Label body;
        private readonly bool outgoing;

        public MessageRow(Label heading, Label body, bool outgoing, bool error)
        {
            this.heading = heading;
            this.body = body;
            this.outgoing = outgoing;
            Margin = new Padding(0, 0, 0, 12);
            bubble = new Surface(outgoing || error ? Rose : Color.White, Line);
            heading.Location = new Point(15, 10);
            body.Location = new Point(15, heading.Top + heading.PreferredHeight + 4);
            bubble.Controls.Add(heading);
            bubble.Controls.Add(body);
            Controls.Add(bubble);
        }

        public void Fit(int width)
        {
            int maxWidth = Math.Min(640, width * 4 / 5);
            body.MaximumSize = new Size(maxWidth - 30, 0);
            Size content = body.PreferredSize;
            int bubbleWidth = Math.Clamp(Math.Max(content.Width, heading.PreferredWidth) + 30, 121, maxWidth);
            Height = body.Top + content.Height + 14;
            bubble.SetBounds(outgoing ? width - bubbleWidth : 0, 0, bubbleWidth, Height);
        }
    }

    private sealed class Surface(Color fill, Color? border) : Panel
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width < 2 || Height < 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int diameter = Math.Min(30, Math.Min(Width - 1, Height - 1));
            using var shape = new GraphicsPath();
            shape.AddArc(0, 0, diameter, diameter, 180, 90);
            shape.AddArc(Width - diameter - 1, 0, diameter, diameter, 270, 90);
            shape.AddArc(Width - diameter - 1, Height - diameter - 1, diameter, diameter, 0, 90);
            shape.AddArc(0, Height - diameter - 1, diameter, diameter, 90, 90);
            shape.CloseFigure();
            using var brush = new SolidBrush(fill);
            e.Graphics.FillPath(brush, shape);
            if (border is Color outline)
            {
                using var pen = new Pen(outline);
                e.Graphics.DrawPath(pen, shape);
            }
        }
    }
}
