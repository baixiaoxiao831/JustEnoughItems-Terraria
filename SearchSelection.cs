using System;
using System.Globalization;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameContent;

namespace JustEnoughItemsTerraria;

public static partial class BrowserUI
{

private sealed class SearchSelection
{
    private static int Pixels => FontPixels(22, BrowserFontFace.Search);
    private static int Padding => Math.Max(1,(int)MathF.Ceiling(4*unit));
    private int anchor,caret,first;
    internal int Caret => caret;
    private string cached="";
    private int[] stops={0};
    private bool dragging;
    private int Left=>Math.Min(anchor,caret);
    private int Right=>Math.Max(anchor,caret);
    private void Layout(string text){
        if(cached==text)return;
        cached=text;stops=StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToArray();
        caret=Boundary(caret);anchor=Boundary(anchor);first=Boundary(first);
    }
    private int Boundary(int position){
        int i=Array.BinarySearch(stops,Math.Clamp(position,0,cached.Length));
        return i>=0?stops[i]:stops[~i-1];
    }
    private int Next(int position,int step)=>stops[Math.Clamp(Array.BinarySearch(stops,position)+step,0,stops.Length-1)];
    private float Width(int start,int end)=>start==end?0:BrowserFont.Measure(cached[start..end],BrowserFontFace.Search,Pixels).X;
    internal void Focus(string text, int? position = null){Layout(text);anchor=caret=Boundary(position ?? text.Length);first=0;dragging=false;}
    internal void SelectAll(string text){Layout(text);anchor=0;caret=text.Length;dragging=false;}
    internal void Mouse(string text,Rectangle field,bool press,bool down,bool shift){
        Layout(text);
        if(!down){dragging=false;return;}
        if(press)dragging=true;
        if(!dragging)return;
        float x=mouse.X-(field.X+Padding);
        if(x<0)caret=Next(first,-1);
        else{
            float limit=Math.Min(x,field.Width-2*Padding);
            int i=first;
            while(i<text.Length){
                int next=Next(i,1);
                if((Width(first,i)+Width(first,next))*.5f>limit)break;
                i=next;
            }
            caret=x>field.Width-2*Padding?Next(i,1):i;
        }
        if(press && !shift)anchor=caret;
    }
    internal string Read(string text){
        Layout(text);
        var keys=Main.inputText;var prev=Main.oldInputText;
        bool Press(Keys k)=>keys.IsKeyDown(k) && prev.IsKeyUp(k);
        bool ctrl=keys.IsKeyDown(Keys.LeftControl)||keys.IsKeyDown(Keys.RightControl);
        bool shift=keys.IsKeyDown(Keys.LeftShift)||keys.IsKeyDown(Keys.RightShift);
        bool selected=anchor!=caret;
        if(ctrl && keys.IsKeyDown(Keys.A)){
            SearchKeyboard.ReadText("");
            if(Press(Keys.A))SelectAll(text);
            return text;
        }
        if(ctrl && (keys.IsKeyDown(Keys.C)||keys.IsKeyDown(Keys.X)||keys.IsKeyDown(Keys.Insert)) ||
           shift && keys.IsKeyDown(Keys.Delete)){
            SearchKeyboard.ReadText(text[Left..Right]);
            if(selected && (Press(Keys.X)||shift && Press(Keys.Delete)))return Replace(text,"");
            return text;
        }
        int direction=Press(Keys.Left)?-1:Press(Keys.Right)?1:0;
        if(direction!=0 || Press(Keys.Home) || Press(Keys.End)){
            SearchKeyboard.ReadText("");
            caret=Press(Keys.Home)?0:Press(Keys.End)?text.Length:selected && !shift?(direction<0?Left:Right):Next(caret,direction);
            if(!shift)anchor=caret;
            return text;
        }
        string prefix=text[..Left];
        string typed=SearchKeyboard.ReadText(prefix);
        if(selected && (keys.IsKeyDown(Keys.Back)||Press(Keys.Delete)))return Replace(text,"");
        if(!selected && Press(Keys.Delete)){anchor=Next(caret,1);return Replace(text,"");}
        if(typed==prefix)return text;
        string updated=typed+text[Right..];
        Layout(updated);anchor=caret=Boundary(typed.Length);return updated;
    }
    private string Replace(string text,string insert){
        int start=Left;
        string next=text[..start]+insert+text[Right..];Layout(next);
        anchor=caret=Boundary(start+insert.Length);return next;
    }
    internal void Draw(SpriteBatch batch,Rectangle field,string text,bool editing,ulong blink){
        Layout(text);
        int width=Math.Max(1,field.Width-2*Padding-ShadowPixels);
        int cursor=editing?caret:text.Length;
        first=Math.Min(first,cursor);
        // Find the first grapheme that fits before the caret, including after a long paste.
        if(Width(first,cursor)>width){
            int lo=Array.BinarySearch(stops,first),hi=Array.BinarySearch(stops,cursor);
            while(lo<hi){int mid=(lo+hi)/2;if(Width(stops[mid],cursor)>width)lo=mid+1;else hi=mid;}
            first=stops[lo];
        }
        int end=first;
        while(end<text.Length){int next=Next(end,1);if(Width(first,next)>width)break;end=next;}
        var pos=new Vector2(field.X+Padding,field.Center.Y);
        int left=Math.Clamp(Left,first,end),right=Math.Clamp(Right,first,end);
        void Part(int a,int b,Color color,Vector2 offset){
            if(a>=b)return;
            string part=text[a..b];
            var top=pos+new Vector2(Width(first,a),-MathF.Round(BrowserFont.Measure(part,BrowserFontFace.Search,Pixels).Y/2))+offset;
            BrowserFont.Draw(batch,part,BrowserFontFace.Search,Pixels,top,color,1);
        }
        if(editing && left<right){
            var fill=TextureAssets.MagicPixel.Value;
            var area=new Rectangle((int)(pos.X+Width(first,left)),field.Y+field.Height/4,
                Math.Max(1,(int)Math.Ceiling(Width(first,right)-Width(first,left))),Math.Max(1,field.Height/2+1));
            batch.Draw(fill,area,new Color(11,40,50));
            Part(first,left,Color.White,Vector2.Zero);
            Part(left,right,new Color(60,88,99),new Vector2(1,1));
            Part(left,right,new Color(208,236,247),Vector2.Zero);
            Part(right,end,Color.White,Vector2.Zero);
        }else{
            Part(first,end,new Color(60,60,60),new Vector2(ShadowPixels));
            Part(first,end,catalog.Length==0 ? Color.Red : Color.White,Vector2.Zero);
            if(editing && blink%60<30){
                var bar=new Rectangle((int)(pos.X+Width(first,Math.Clamp(caret,first,end))),field.Y+4,1,Math.Max(1,field.Height-8));
                batch.Draw(TextureAssets.MagicPixel.Value,bar,Color.White);
            }
        }
    }
}

}
