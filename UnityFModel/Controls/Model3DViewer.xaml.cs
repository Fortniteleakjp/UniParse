using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace UnityFModel.Controls;

/// <summary>A minimal trackball 3D viewer that renders a single <see cref="MeshGeometry3D"/>.</summary>
public partial class Model3DViewer : UserControl
{
    public static readonly DependencyProperty GeometryProperty =
        DependencyProperty.Register(nameof(Geometry), typeof(MeshGeometry3D), typeof(Model3DViewer),
            new PropertyMetadata(null, OnGeometryChanged));

    public MeshGeometry3D? Geometry
    {
        get => (MeshGeometry3D?)GetValue(GeometryProperty);
        set => SetValue(GeometryProperty, value);
    }

    private readonly AxisAngleRotation3D _yaw = new(new Vector3D(0, 1, 0), 20);
    private readonly AxisAngleRotation3D _pitch = new(new Vector3D(1, 0, 0), -20);
    private Point3D _center;
    private double _radius = 1;
    private double _zoom = 1;
    private Point _lastMouse;
    private bool _dragging;

    public Model3DViewer()
    {
        InitializeComponent();
    }

    private static void OnGeometryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((Model3DViewer)d).Rebuild();

    private void Rebuild()
    {
        if (Geometry is null)
        {
            MeshVisual.Content = null;
            return;
        }

        Rect3D bounds = Geometry.Bounds;
        _center = new Point3D(bounds.X + bounds.SizeX / 2, bounds.Y + bounds.SizeY / 2, bounds.Z + bounds.SizeZ / 2);
        _radius = 0.5 * Math.Sqrt((bounds.SizeX * bounds.SizeX) + (bounds.SizeY * bounds.SizeY) + (bounds.SizeZ * bounds.SizeZ));
        if (_radius <= 0 || double.IsNaN(_radius))
            _radius = 1;
        _zoom = 1;
        _yaw.Angle = 20;
        _pitch.Angle = -20;

        DiffuseMaterial material = new(new SolidColorBrush(Color.FromRgb(0xC2, 0xC7, 0xD0)));
        GeometryModel3D model = new(Geometry, material) { BackMaterial = material };

        Transform3DGroup transform = new();
        transform.Children.Add(new RotateTransform3D(_pitch, _center));
        transform.Children.Add(new RotateTransform3D(_yaw, _center));
        model.Transform = transform;

        MeshVisual.Content = model;
        UpdateCamera();
    }

    private void UpdateCamera()
    {
        double distance = _radius / Math.Tan(Camera.FieldOfView / 2 * Math.PI / 180) * 1.4 * _zoom;
        if (distance <= 0 || double.IsNaN(distance))
            distance = 3;
        Camera.Position = new Point3D(_center.X, _center.Y, _center.Z + distance);
        Camera.LookDirection = new Vector3D(0, 0, -1);
        Camera.UpDirection = new Vector3D(0, 1, 0);
        Camera.NearPlaneDistance = Math.Max(0.01, distance - (_radius * 3));
        Camera.FarPlaneDistance = distance + (_radius * 4);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _dragging = true;
        _lastMouse = e.GetPosition(this);
        Focus();
        CaptureMouse();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragging = false;
        ReleaseMouseCapture();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging)
            return;
        Point p = e.GetPosition(this);
        _yaw.Angle += (p.X - _lastMouse.X) * 0.5;
        _pitch.Angle = Math.Clamp(_pitch.Angle + ((p.Y - _lastMouse.Y) * 0.5), -89, 89);
        _lastMouse = p;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        _zoom *= e.Delta > 0 ? 0.9 : 1.1;
        _zoom = Math.Clamp(_zoom, 0.1, 10);
        UpdateCamera();
    }
}
