// Erstatninger for de få WPF-typene som programkjernen bruker, så stresstesten kan kjøres uten Windows.
namespace System.Windows.Threading
{
    public class Dispatcher
    {
        public static Dispatcher CurrentDispatcher { get; } = new();
        public void BeginInvoke(Action a) => a();
    }
}

namespace System.Windows
{
    public class Application
    {
        public static Application? Current => null;
        public Threading.Dispatcher Dispatcher { get; } = new();
    }
}
