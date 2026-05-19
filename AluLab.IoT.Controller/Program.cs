using System.Threading;

namespace AluLab.IoT.Controller
{
	internal class Program
	{
		static void Main()
		{
			Board board_ = new();

			board_.Initialize();

			Thread.Sleep( Timeout.Infinite );

		}
	}
}
