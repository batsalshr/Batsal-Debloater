// WPF projects exclude System.IO from implicit usings because System.Windows.Shapes.Path would clash with System.IO.Path.
// No code in this project imports System.Windows.Shapes, so it is safe to use globally.
global using System.IO;
