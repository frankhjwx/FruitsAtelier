using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L=FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class HitsoundCopierRenderCheck
{
    internal static void Run(D2DCanvas canvas,int width,int height)
    {
        string language=L.Language;
        try
        {
            foreach(string lang in new[]{"en","zh-CN"})
            {
                L.SetLanguage(lang);
                var view=new EditorView(loadDemo:false);
                var map=new MapDocument {IsDemo=false}; map.Fruits.Add(new Fruit {TimeMs=1000,X=100});
                view.LoadDocument(map);
                void Paint(){canvas.Begin();view.Render(canvas,width,height);canvas.End();}
                view.OpenHitsoundCopier(); Paint();
                var r=view.HitsoundCopierBounds;
                if(!view.HitsoundCopierVisible || r.X<0 || r.Y<0 || r.Right>width || r.Bottom>height)
                    throw new InvalidOperationException("Hitsound Copier does not fit the native window.");
                foreach(int mode in new[]{2,0,1})
                {
                    float x=r.X+32+mode*(r.Width-44)/3,y=r.Y+70;
                    view.PointerDown(x,y,0,false,false);view.PointerUp(x,y,0);Paint();
                }
                view.PointerDown(r.X+32,r.Y+278,0,false,false); view.PointerUp(r.X+32,r.Y+278,0); Paint();
                float newX=r.X+38+(r.Width-50)/2;
                view.PointerDown(newX,r.Y+238,0,false,false); view.PointerUp(newX,r.Y+238,0); Paint();
                view.KeyDown(27,false,false);Paint();
                if(view.HitsoundCopierVisible || !view.Document.ContentEquals(map))
                    throw new InvalidOperationException("Cancelling Hitsound Copier changed map content.");
            }
        }
        finally{L.SetLanguage(language);}
    }
}
