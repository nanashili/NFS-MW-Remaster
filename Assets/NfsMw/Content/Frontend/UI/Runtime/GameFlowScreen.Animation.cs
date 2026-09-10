using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace NfsMwRemaster.Driving
{
    public sealed partial class GameFlowScreen
    {
        private sealed class FrontendAnimation
        {
            public VisualElement element;
            public float start, duration;
            public Action<float> draw;
            public Action completed;
        }
        private readonly List<FrontendAnimation> animations = new List<FrontendAnimation>();
        private MostWantedFrontendPage carouselPage = (MostWantedFrontendPage)(-1);
        private int carouselSelection;

        private void Animate(VisualElement element,float duration,Action<float> draw,float delay=0,Action completed=null)
        {
            draw(0);
            animations.Add(new FrontendAnimation { element=element,start=Time.unscaledTime+delay,duration=duration,draw=draw,completed=completed });
        }

        private void TickFrontendAnimations()
        {
            for (int i=animations.Count-1;i>=0;i--)
            {
                var animation=animations[i];
                if (animation.element.panel == null) { animations.RemoveAt(i); continue; }
                float t=Mathf.Clamp01((Time.unscaledTime-animation.start)/animation.duration);
                animation.draw(t*t*(3-2*t));
                if(t<1)continue;
                animations.RemoveAt(i);animation.completed?.Invoke();
            }
        }

        private void BeginPageAnimation(VisualElement oldContent,VisualElement oldFooter,bool changed)
        {
            if(!changed)return;
            animations.RemoveAll(animation => animation.element==oldContent || animation.element==oldFooter
                || (oldContent!=null && oldContent.Contains(animation.element)) || (oldFooter!=null && oldFooter.Contains(animation.element)));
            FadeOut(oldContent);FadeOut(oldFooter);
            var incoming=content; var commands=footer;
            bool carousel=navigation.Current.Page==MostWantedFrontendPage.MainMenu || navigation.Current.Page==MostWantedFrontendPage.Career || navigation.Current.Page==MostWantedFrontendPage.Options;
            float delay=carousel && oldContent!=null?.60f:.14f;
            if(runtime.Flow.State==GameFlowState.MainMenu && oldContent?.Q<Button>("alias-no")!=null)delay=.14f;
            Animate(incoming,.26f,t=>{incoming.style.opacity=t;incoming.style.translate=new Translate(0,(1-t)*18);},delay);
            Animate(commands,.22f,t=>commands.style.opacity=t,delay+.06f);
            void FadeOut(VisualElement outgoing)
            {
                if(outgoing==null)return;
                outgoing.RemoveFromHierarchy();surface.Add(outgoing);
                outgoing.pickingMode=PickingMode.Ignore;outgoing.focusable=false;
                outgoing.Query<VisualElement>().ForEach(element=>{element.pickingMode=PickingMode.Ignore;element.focusable=false;});
                Animate(outgoing,.18f,t=>outgoing.style.opacity=1-t,completed:()=>outgoing.RemoveFromHierarchy());
            }
        }

        private void AnimateCarouselIcon(VisualElement icon,int from,int to,float finalSize,float finalOpacity)
        {
            if(from==to)return;
            float fromSize=from==0?142:Mathf.Abs(from)==1?96:62;
            float fromOpacity=from==0?1:Mathf.Abs(from)==1?.62f:Mathf.Abs(from)==2?.28f:0;
            float offset=CarouselOffset(from)-CarouselOffset(to);
            Animate(icon,.18f,t=>
            {
                float scale=Mathf.Lerp(fromSize,finalSize,t)/finalSize;
                icon.style.translate=new Translate(offset*(1-t),0);
                icon.style.scale=new Scale(new Vector3(scale,scale,1));
                icon.style.opacity=Mathf.Lerp(fromOpacity,finalOpacity,t);
            });
        }

        private static float CarouselOffset(int relative) => relative==0?0:Mathf.Sign(relative)*(Mathf.Abs(relative)==1?128:Mathf.Abs(relative)*96+32);
    }
}
