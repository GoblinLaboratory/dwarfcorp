using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace DwarfCorp.Gui.Widgets
{
    public class Button : Widget
    {
        private Vector4 previousTextColor = Color.Black.ToVector4();
        public override void Construct()
        {
            TextVerticalAlign = VerticalAlign.Center;
            TextHorizontalAlign = HorizontalAlign.Center;
            

            if (string.IsNullOrEmpty(Border))
            {
                Border = "border-button";
            }

            if (Border == "none")
            {
                Border = null;
            }


            previousTextColor = TextColor;
            OnMouseEnter += (widget, action) =>
            {
                previousTextColor = TextColor;
                widget.TextColor = new Vector4(0.5f, 0, 0, 1.0f);
                widget.Invalidate();
            };

            OnMouseLeave += (widget, action) =>
            {
                widget.TextColor = previousTextColor;
                widget.Invalidate();
            };
        }
    }

    public class ImageButton : Widget
    {
        public override void Construct()
        {
            var color = BackgroundColor;
            OnMouseEnter += (widget, action) =>
            {
                widget.BackgroundColor = new Vector4(0.5f, 0, 0, 1.0f);
                widget.Invalidate();
            };

            OnMouseLeave += (widget, action) =>
            {
                widget.BackgroundColor = color;
                widget.Invalidate();
            };
        }
    }
}
