
namespace AluLab.Board.Touch
{
	public sealed class InputService
	{
		/// <summary>
		/// Represents a single point of contact in a touch interface, defined by its X and Y coordinates.
		/// </summary>
		/// <param name="X">The horizontal position of the touch point, measured in pixels from the left edge.</param>
		/// <param name="Y">The vertical position of the touch point, measured in pixels from the top edge.</param>
		public record TouchPoint(int X, int Y);

		public event Action<TouchPoint>? TouchDown;
		public event Action<TouchPoint>? TouchMove;
		public event Action<TouchPoint>? TouchUp;

		/// <summary>
		/// Raises the touch down event at the specified screen coordinates.
		/// </summary>
		/// <param name="x">The horizontal position, in pixels, where the touch occurred.</param>
		/// <param name="y">The vertical position, in pixels, where the touch occurred.</param>
		public void OnTouchDown(int x, int y) => TouchDown?.Invoke(new TouchPoint(x, y));
		public void OnTouchMove(int x, int y) => TouchMove?.Invoke(new TouchPoint(x, y));
		public void OnTouchUp(int x, int y) => TouchUp?.Invoke(new TouchPoint(x, y));
	}
}