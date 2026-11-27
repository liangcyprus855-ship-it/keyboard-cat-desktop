using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// A build-time exporter. The reference WPF assemblies and sources are only required on Windows.
internal static class ExportArt
{
    const BindingFlags StaticMember = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags ScalarMember = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const int Scale = 2;
    static string output;
    static readonly Dictionary<string, object> sprites = new Dictionary<string, object>();
    static readonly Dictionary<string, object> dogs = new Dictionary<string, object>();
    static readonly Dictionary<string, object> keys = new Dictionary<string, object>();
    static readonly Dictionary<string, double[]> appleKeys = new Dictionary<string, double[]>();
    static readonly Type Ginger = typeof(CatArt).GetNestedType("Ginger", BindingFlags.NonPublic);

    static T Field<T>(Type type,string name) { return (T)type.GetField(name,StaticMember).GetValue(null); }
    static void Call(Type type,string name,params object[] args) { type.GetMethod(name,StaticMember).Invoke(null,args); }
    static double[] Position(Point value) { return new double[] {value.X,value.Y}; }

    static void Sprite(string id,Action<DrawingContext> paint)
    {
        var drawing=new DrawingGroup();
        using(DrawingContext dc=drawing.Open()) paint(dc);
        Rect b=drawing.Bounds;
        if(b.IsEmpty) throw new Exception("Empty sprite: "+id);
        int x=(int)Math.Floor(b.Left)-1,y=(int)Math.Floor(b.Top)-1;
        int w=(int)Math.Ceiling(b.Right)+1-x,h=(int)Math.Ceiling(b.Bottom)+1-y;
        var visual=new DrawingVisual();
        using(DrawingContext dc=visual.RenderOpen()){
            dc.PushTransform(new TranslateTransform(-x,-y));
            dc.DrawDrawing(drawing);
            dc.Pop();
        }
        var bmp=new RenderTargetBitmap(w*Scale,h*Scale,96*Scale,96*Scale,PixelFormats.Pbgra32);
        bmp.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));
        using(var stream=File.Create(Path.Combine(output,id+".png"))) encoder.Save(stream);
        sprites.Add(id,new {file=id+".png",x=x,y=y,w=w,h=h});
    }

    static void Cats()
    {
        int[] petIds = { 0, 1, 2, 3, 14, 15, 16, 17 };
        foreach (int c in petIds)
        {
            int cat=c;
            bool isDog=DogArt.IsDog((CatStyle)cat);
            bool ginger=c==1;
            Type owner=c<2?typeof(CatArt):c==2?typeof(BobtailArt):typeof(TabbyArt);
            double height=c==3?1.025:1, width=c==3?1.07:.92, bodyTop=237-119*height;
            Sprite("body-"+c, delegate(DrawingContext dc){
                if(isDog){
                    DogArt.For((CatStyle)cat).DrawBodyLayer(dc);
                } else if(cat<2){
                    Geometry body=Field<Geometry>(typeof(CatArt),"Body");
                    dc.DrawGeometry(ginger?Field<Brush>(Ginger,"BodyFur"):Field<Brush>(owner,"Fur"),null,body);
                    if(ginger)Call(Ginger,"DrawBodyMarkings",dc,body);
                } else{
                    dc.PushTransform(new TranslateTransform(214,bodyTop));dc.PushTransform(new ScaleTransform(width,height));
                    dc.DrawDrawing(Field<DrawingGroup>(owner,"BodyCoat"));dc.Pop();dc.Pop();
                }
            });
            if(c==3)Sprite("ruff-3",delegate(DrawingContext dc){
                dc.PushTransform(new TranslateTransform(214,bodyTop));dc.PushTransform(new ScaleTransform(width,height));
                dc.DrawGeometry(Field<Brush>(owner,"RuffFur"),null,Field<Geometry>(owner,"Ruff"));dc.Pop();dc.Pop();
            });
            for(int phase=0;phase<8;phase++){
                double angular=2*Math.PI*phase/8;
                Sprite("tail-"+c+"-"+phase, delegate(DrawingContext dc){
                    if(isDog){DogArt.For((CatStyle)cat).DrawTailLayer(dc,angular);}
                    else if(cat<2){Call(owner,"Tail",dc,angular/1.9,ginger);}
                    else{
                        bool tabby=cat==3;
                        dc.PushTransform(new TranslateTransform(tabby?187:249,tabby?184:207));
                        dc.PushTransform(new RotateTransform((tabby?186:0)+Math.Sin(angular)*(tabby?2.1:3)));
                        dc.PushTransform(new ScaleTransform(tabby?1.1:1,tabby?1.22:1));
                        dc.DrawDrawing(Field<DrawingGroup>(owner,"TailCoat"));dc.Pop();dc.Pop();dc.Pop();
                    }
                });
            }
            for(int ix=0;ix<3;ix++)for(int iy=0;iy<3;iy++)for(int lid=0;lid<3;lid++){
                double gx=ix-1.0,gy=iy-1.0,blink=lid*.5;
                Sprite("head-"+c+"-"+ix+"-"+iy+"-"+lid,delegate(DrawingContext dc){
                    if(isDog){DogArt.For((CatStyle)cat).DrawHeadLayer(dc,gx,gy,blink);}
                    else if(cat<2) Call(owner,"DrawHead",dc,ginger,gx,gy,blink);
                    else{
                        dc.PushTransform(new ScaleTransform(cat==2?.95:1.055,cat==2?.99:1));
                        Call(owner,"DrawHead",dc,gx,gy,blink);
                        dc.Pop();
                    }
                });
            }
            if(isDog){
                var dog=DogArt.For((CatStyle)cat);
                Sprite("dog-ruff-"+c,delegate(DrawingContext dc){dog.DrawRuffLayer(dc);});
                var lSegs=dog.GetSegments(true);
                var rSegs=dog.GetSegments(false);
                var lMeta=new object[lSegs.Length];
                var rMeta=new object[rSegs.Length];
                for(int s=0;s<lSegs.Length;s++){
                    int seg=s;
                    Sprite("dog-seg-l-"+c+"-"+s,delegate(DrawingContext dc){dog.DrawSleeveSegLayer(dc,true,seg);});
                    Sprite("dog-seg-r-"+c+"-"+s,delegate(DrawingContext dc){dog.DrawSleeveSegLayer(dc,false,seg);});
                    lMeta[s]=new {t0=lSegs[s].T0,t1=lSegs[s].T1,span=lSegs[s].Span};
                    rMeta[s]=new {t0=rSegs[s].T0,t1=rSegs[s].T1,span=rSegs[s].Span};
                }
                Sprite("dog-paw-l-"+c,delegate(DrawingContext dc){dog.DrawPawLayer(dc,true);});
                Sprite("dog-paw-r-"+c,delegate(DrawingContext dc){dog.DrawPawLayer(dc,false);});
                dogs.Add(c.ToString(),new {
                    shoulderL=Position(dog.ShoulderLeft),
                    restPawL=Position(dog.RestPawLeft),
                    shoulderR=Position(dog.ShoulderRight),
                    restPawR=Position(dog.RestPawRight),
                    tailSpeed=dog.TailSpeed,
                    tailPivot=Position(dog.TailPivotPoint),
                    wagDeg=dog.TailWagDeg,
                    segsL=lMeta,
                    segsR=rMeta
                });
            }
            Sprite("portrait-"+c,delegate(DrawingContext dc){CatArt.DrawPortrait(dc,(CatStyle)cat);});
        }
        for(int c=4;c<=13;c++){
            CatStyle person=(CatStyle)c;
            Sprite("portrait-"+c,delegate(DrawingContext dc){CatArt.DrawPortrait(dc,person);});
        }
    }

    static void Peripherals()
    {
        for(int vk=0;vk<256;vk++)keys.Add(vk.ToString(),Position(DeskArt.KeyPoint(vk)));
        IDictionary surfaces=Field<IDictionary>(typeof(DeskArt),"Surfaces");
        MethodInfo lookup=typeof(DeskArt).GetMethod("Lookup",StaticMember);
        // 256..261 cover Globe/Fn, regional keys, and the compact keyboard's Lock key.
        const int lastAppleKey=261;
        for(int vk=0;vk<=lastAppleKey;vk++)
            appleKeys.Add(vk.ToString(),Position(DeskArt.KeyPoint(KeyboardStyle.Apple,vk)));
        foreach(KeyboardStyle style in Enum.GetValues(typeof(KeyboardStyle))){
            int i=(int)style;
            Sprite("keyboard-"+i,delegate(DrawingContext dc){DeskArt.DrawKeyboard(dc,style,null);});
            object surface=surfaces[style];
            var lights=(IDictionary)surface.GetType().GetField("Lights",ScalarMember).GetValue(surface);
            foreach(DictionaryEntry item in lights){
                int code=(int)item.Key;
                var overlay=(DrawingGroup)item.Value;
                Sprite("key-"+i+"-"+code,delegate(DrawingContext dc){
                    dc.PushTransform(new TranslateTransform(0,DeskLayout.Forward));
                    dc.DrawDrawing(overlay);dc.Pop();
                });
            }
            // Aliases share one PNG and use the same projection as the live WPF keyboard.
            int lastKey=style==KeyboardStyle.Apple ? lastAppleKey : 255;
            for(int vk=0;vk<=lastKey;vk++){
                string id="key-"+i+"-"+vk;
                if(sprites.ContainsKey(id))continue;
                object key=lookup.Invoke(null,new object[]{style,vk});
                if(key==null)continue;
                int keyCode=(int)key.GetType().GetField("Code",ScalarMember).GetValue(key);
                sprites.Add(id,sprites["key-"+i+"-"+keyCode]);
            }
        }
        foreach(MouseStyle mouse in Enum.GetValues(typeof(MouseStyle))){
            int i=(int)mouse;
            foreach(string state in new[]{"normal","left","right","both"}){
                bool l=state=="left"||state=="both",r=state=="right"||state=="both";
                Sprite("mouse-"+i+"-"+state,delegate(DrawingContext dc){MouseArt.Draw(dc,mouse,l?1:0,r?1:0);});
            }
        }
    }

    [STAThread]public static int Main(string[] args)
    {
        output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
        Cats();Peripherals();
        var keysByStyle=new Dictionary<string,Dictionary<string,double[]>>();
        keysByStyle.Add(((int)KeyboardStyle.Apple).ToString(),appleKeys);
        var manifest=new {scale=Scale,sceneWidth=480,sceneHeight=320,mouseCenter=Position(MouseArt.Center),mousePaw=Position(MouseArt.PawTarget),sprites=sprites,dogs=dogs,keys=keys,keysByStyle=keysByStyle};
        File.WriteAllText(Path.Combine(output,"atlas.json"),new JavaScriptSerializer().Serialize(manifest),new System.Text.UTF8Encoding(false));
        Console.WriteLine("{0} sprite IDs, {1} transparent PNGs, {2} standard and {3} Apple key locations.",
            sprites.Count,Directory.GetFiles(output,"*.png").Length,keys.Count,appleKeys.Count);
        return 0;
    }
}
