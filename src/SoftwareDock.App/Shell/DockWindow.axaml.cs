using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Options;
using SoftwareDock.App.ViewModels;

namespace SoftwareDock.App.Shell;

/// <summary>
/// The panel. Anchored (📌) it hugs the right edge of the screen, full height, and hides when it
/// loses focus — a flyout. Floating (🪟) it is a normal draggable window that stays put.
/// </summary>
public partial class DockWindow : Window
{
    private const double FloatingHeight = 680;

    private readonly DockViewModel _viewModel;
    private readonly DockOptions _options;
    private PixelPoint? _floatingPosition;

    public DockWindow()
        : this(null!, Options.Create(new DockOptions()))
    {
        // Design-time only.
    }

    public DockWindow(DockViewModel viewModel, IOptions<DockOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _viewModel = viewModel;
        _options = options.Value;
        DataContext = viewModel;
        AvaloniaXamlLoader.Load(this);

        if (viewModel is not null)
        {
            viewModel.CloseRequested += (_, _) => Hide();
            viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DockViewModel.IsPinned))
                {
                    ApplyPlacement();
                }
            };
        }

        this.FindControl<Border>("Header")!.PointerPressed += OnHeaderPointerPressed;
        PositionChanged += (_, e) =>
        {
            if (_viewModel?.IsPinned == false)
            {
                _floatingPosition = e.Point;
            }
        };
        Deactivated += (_, _) =>
        {
            if (_viewModel?.IsPinned == true)
            {
                Hide();
            }
        };
    }

    public void ShowDock()
    {
        ApplyPlacement();
        Show();
        Activate();
        this.FindControl<TextBox>("SearchBox")?.Focus();
        _ = _viewModel.LoadAsync();
    }

    public void ToggleDock()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            ShowDock();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            this.FindControl<TextBox>("SearchBox")?.Focus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F5)
        {
            _viewModel.RefreshCommand.Execute(null);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void ApplyPlacement()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null)
        {
            return;
        }

        if (_viewModel.IsPinned)
        {
            var (position, size) = DockPlacement.Anchored(screen.WorkingArea, screen.Scaling, _options.Width, _options.Margin);
            Width = size.Width;
            Height = size.Height;
            Position = position;
        }
        else
        {
            Width = _options.Width;
            Height = Math.Min(FloatingHeight, screen.WorkingArea.Height / screen.Scaling);
            Position = _floatingPosition ?? new PixelPoint(
                screen.WorkingArea.X + (int)((screen.WorkingArea.Width - (Width * screen.Scaling)) / 2),
                screen.WorkingArea.Y + (int)((screen.WorkingArea.Height - (Height * screen.Scaling)) / 2));
        }
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel?.IsPinned == false && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
}
