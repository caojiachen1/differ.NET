using Android.Views;
using Java.Lang;
using System;
using System.Collections.Generic;
using System.Linq;

namespace differ.NET.Android;

/// <summary>
/// 触摸交互处理器，处理移动端的手势操作
/// </summary>
public class TouchInteractionHandler
{
    private readonly Dictionary<int, TouchInfo> _activeTouches = new();
    private readonly GestureDetector _gestureDetector;
    private readonly ITouchInteractionListener _listener;

    private class TouchInfo
    {
        public long StartTime { get; set; }
        public float StartX { get; set; }
        public float StartY { get; set; }
        public float CurrentX { get; set; }
        public float CurrentY { get; set; }
    }

    /// <summary>
    /// 触摸交互监听器接口
    /// </summary>
    public interface ITouchInteractionListener
    {
        /// <summary>
        /// 长按事件
        /// </summary>
        void OnLongPress(float x, float y, int pointerId);

        /// <summary>
        /// 点击事件
        /// </summary>
        void OnTap(float x, float y, int pointerId);

        /// <summary>
        /// 双击事件
        /// </summary>
        void OnDoubleTap(float x, float y, int pointerId);

        /// <summary>
        /// 滑动事件
        /// </summary>
        void OnSwipe(float startX, float startY, float endX, float endY, SwipeDirection direction);

        /// <summary>
        /// 捏合缩放事件
        /// </summary>
        void OnPinchZoom(float scaleFactor, float focusX, float focusY);
    }

    /// <summary>
    /// 滑动方向
    /// </summary>
    public enum SwipeDirection
    {
        None,
        Left,
        Right,
        Up,
        Down
    }

    /// <summary>
    /// 长按时间阈值（毫秒）
    /// </summary>
    private const int LONG_PRESS_THRESHOLD = 500;

    /// <summary>
    /// 滑动最小距离（像素）
    /// </summary>
    private const float SWIPE_THRESHOLD = 50f;

    /// <summary>
    /// 双击时间间隔阈值（毫秒）
    /// </summary>
    private const int DOUBLE_TAP_THRESHOLD = 300;

    private long _lastTapTime;
    private float _lastTapX;
    private float _lastTapY;

    public TouchInteractionHandler(ITouchInteractionListener listener)
    {
        _listener = listener ?? throw new ArgumentNullException(nameof(listener));
        _gestureDetector = new GestureDetector(new GestureListener(this));
    }

    /// <summary>
    /// 处理触摸事件
    /// </summary>
    public bool OnTouchEvent(MotionEvent e)
    {
        _gestureDetector.OnTouchEvent(e);

        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
            case MotionEventActions.PointerDown:
                HandleTouchStart(e);
                break;

            case MotionEventActions.Move:
                HandleTouchMove(e);
                break;

            case MotionEventActions.Up:
            case MotionEventActions.PointerUp:
            case MotionEventActions.Cancel:
                HandleTouchEnd(e);
                break;
        }

        return true;
    }

    private void HandleTouchStart(MotionEvent e)
    {
        var pointerIndex = e.ActionIndex;
        var pointerId = e.GetPointerId(pointerIndex);
        var x = e.GetX(pointerIndex);
        var y = e.GetY(pointerIndex);

        _activeTouches[pointerId] = new TouchInfo
        {
            StartTime = JavaSystem.CurrentTimeMillis(),
            StartX = x,
            StartY = y,
            CurrentX = x,
            CurrentY = y
        };
    }

    private void HandleTouchMove(MotionEvent e)
    {
        for (int i = 0; i < e.PointerCount; i++)
        {
            var pointerId = e.GetPointerId(i);
            if (_activeTouches.ContainsKey(pointerId))
            {
                _activeTouches[pointerId].CurrentX = e.GetX(i);
                _activeTouches[pointerId].CurrentY = e.GetY(i);
            }
        }

        // 检测捏合缩放
        if (e.PointerCount >= 2)
        {
            DetectPinchZoom(e);
        }
    }

    private void HandleTouchEnd(MotionEvent e)
    {
        var pointerIndex = e.ActionIndex;
        var pointerId = e.GetPointerId(pointerIndex);

        if (_activeTouches.ContainsKey(pointerId))
        {
            var touchInfo = _activeTouches[pointerId];
            var currentTime = JavaSystem.CurrentTimeMillis();
            var duration = currentTime - touchInfo.StartTime;
            var deltaX = touchInfo.CurrentX - touchInfo.StartX;
            var deltaY = touchInfo.CurrentY - touchInfo.StartY;
            var distance = System.Math.Sqrt(deltaX * deltaX + deltaY * deltaY);

            // 检测长按
            if (duration >= LONG_PRESS_THRESHOLD && distance < 10)
            {
                _listener.OnLongPress(touchInfo.StartX, touchInfo.StartY, pointerId);
            }
            // 检测滑动
            else if (distance > SWIPE_THRESHOLD)
            {
                var direction = GetSwipeDirection(deltaX, deltaY);
                _listener.OnSwipe(touchInfo.StartX, touchInfo.StartY, 
                    touchInfo.CurrentX, touchInfo.CurrentY, direction);
            }
            // 检测点击
            else if (distance < 10)
            {
                // 检测双击
                if (currentTime - _lastTapTime < DOUBLE_TAP_THRESHOLD &&
                    System.Math.Abs(touchInfo.StartX - _lastTapX) < 20 &&
                    System.Math.Abs(touchInfo.StartY - _lastTapY) < 20)
                {
                    _listener.OnDoubleTap(touchInfo.StartX, touchInfo.StartY, pointerId);
                    _lastTapTime = 0; // 重置双击计时
                }
                else
                {
                    _listener.OnTap(touchInfo.StartX, touchInfo.StartY, pointerId);
                    _lastTapTime = currentTime;
                    _lastTapX = touchInfo.StartX;
                    _lastTapY = touchInfo.StartY;
                }
            }

            _activeTouches.Remove(pointerId);
        }
    }

    private void DetectPinchZoom(MotionEvent e)
    {
        if (e.PointerCount >= 2)
        {
            var x1 = e.GetX(0);
            var y1 = e.GetY(0);
            var x2 = e.GetX(1);
            var y2 = e.GetY(1);

            var distance = System.Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
            
            // 这里可以实现更复杂的捏合缩放检测逻辑
            // 简化版本：根据距离变化触发缩放事件
            if (distance > 50) // 最小距离阈值
            {
                var focusX = (x1 + x2) / 2;
                var focusY = (y1 + y2) / 2;
                var scaleFactor = (float)(distance / 100.0); // 简化的缩放因子
                
                _listener.OnPinchZoom(scaleFactor, focusX, focusY);
            }
        }
    }

    private SwipeDirection GetSwipeDirection(float deltaX, float deltaY)
    {
        if (System.Math.Abs(deltaX) > System.Math.Abs(deltaY))
        {
            return deltaX > 0 ? SwipeDirection.Right : SwipeDirection.Left;
        }
        else
        {
            return deltaY > 0 ? SwipeDirection.Down : SwipeDirection.Up;
        }
    }

    /// <summary>
    /// 手势监听器
    /// </summary>
    private class GestureListener : GestureDetector.SimpleOnGestureListener
    {
        private readonly TouchInteractionHandler _handler;

        public GestureListener(TouchInteractionHandler handler)
        {
            _handler = handler;
        }

        public override bool OnDown(MotionEvent? e)
        {
            return true;
        }

        public override bool OnSingleTapUp(MotionEvent? e)
        {
            // 已在HandleTouchEnd中处理
            return true;
        }

        public override void OnLongPress(MotionEvent? e)
        {
            // 已在HandleTouchEnd中处理
        }

        public override bool OnScroll(MotionEvent? e1, MotionEvent? e2, float distanceX, float distanceY)
        {
            // 已在HandleTouchEnd中处理滑动检测
            return true;
        }
    }

    /// <summary>
    /// 重置所有触摸状态
    /// </summary>
    public void Reset()
    {
        _activeTouches.Clear();
        _lastTapTime = 0;
    }

    /// <summary>
    /// 获取活动触摸点数量
    /// </summary>
    public int GetActiveTouchCount()
    {
        return _activeTouches.Count;
    }

    /// <summary>
    /// 是否正在处理长按
    /// </summary>
    public bool IsHandlingLongPress()
    {
        var currentTime = JavaSystem.CurrentTimeMillis();
        return _activeTouches.Values.Any(touch => 
            currentTime - touch.StartTime >= LONG_PRESS_THRESHOLD);
    }
}