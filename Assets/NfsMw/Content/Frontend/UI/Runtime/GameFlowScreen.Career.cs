using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using static NfsMwRemaster.Driving.MostWantedFrontendStyle;

namespace NfsMwRemaster.Driving
{
    public sealed partial class GameFlowScreen
    {
        private CareerProfileData careerSnapshot;
        private VehicleCustomizationDefinition pendingPaint;
        private int rivalIndex;
        private VehicleCustomizationSystem Customization => runtime.WorldSession?.GetComponent<VehicleCustomizationSystem>();
        private int Bounty => runtime.WorldSession?.GetComponent<VehicleBountySystem>()?.TotalBounty??0;
        private void RefreshCareerSnapshot()
        { careerSnapshot=runtime.WorldSession?.GetComponent<CareerProfileSystem>()?.CurrentProfile; }
        private string CompletionLabel()=>"—"; // The career service does not yet publish whole-campaign completion.
        private MostWantedFrontendContent.Rival Rival => frontendContent?.rivals!=null&&frontendContent.rivals.Length>0
            ? frontendContent.rivals[Mathf.Clamp(rivalIndex,0,frontendContent.rivals.Length-1)]:null;
        private int CompletedMilestones => runtime.WorldSession?.Events.Count(e=>e!=null&&e.Kind==FreeRoamEventKind.Pursuit
            &&runtime.WorldSession.CompletedEvents.Contains(e.Id))??0;

        private void RenderBlacklist()
        {
            var rival=Rival;Header(rival==null?"blacklist":"blacklist "+rival.rank,true);
            if(rival==null)
            {
                Panel(content,660,260,722,420,false,false);
                Text(content,"No rival roster is published.",712,331,610,75,36,Khaki);
                Text(content,"Rival artwork and metadata can be assigned in the frontend Content asset. Career results are never invented by the menu.",712,421,610,161,28);
            }
            else
            {
                Text(content,rival.displayName,224,111,480,70,42);
                var portrait=MostWantedFrontendArt.Find(rival.portraitTexture,"rival-"+rival.id);
                if(portrait!=null)Image(content,portrait,108,149,681,790,null,ScaleMode.ScaleToFit);
                AddGrunge(content,622,201,936,754,"rival");
                Text(content,"You know what you've got to do.",810,286,619,81,31,Khaki);
                var stats=Panel(content,710,493,666,226,false,false,new Color(0,0,0,.82f));
                Text(stats,"Current",426,6,217,55,32,Color.white,TextAnchor.MiddleRight);
                Stat(stats,"Race Wins",careerSnapshot?.statistics.racesWon??0,69);
                Stat(stats,"Milestones Completed",CompletedMilestones,123);
                Stat(stats,"Total Bounty",Bounty,177);
                bool defeated=rival.challengeEventIds.Length>0&&runtime.WorldSession!=null
                    &&rival.challengeEventIds.All(id=>runtime.WorldSession.CompletedEvents.Contains(id));
                if(defeated)
                {
                    var stamp=Box(content,"rival-defeated",198,733,412,112);stamp.style.rotate=new Rotate(-6);
                    stamp.style.backgroundColor=new Color(.55f,0,0,.8f);Text(stamp,"DEFEATED",15,5,382,98,57,Color.red,TextAnchor.MiddleCenter);
                }
            }
            Carousel("",new[]{new MenuItem("Race Events","flag",()=>OpenPage(MostWantedFrontendPage.RaceEvents)),
                new MenuItem("Milestones","badge",()=>OpenPage(MostWantedFrontendPage.Milestones)),
                new MenuItem("Bounty","bounty",()=>OpenPage(MostWantedFrontendPage.Bounty)),
                new MenuItem("Rival Bio","blacklist",()=>OpenPage(MostWantedFrontendPage.RivalBio))});
            Footer("Back",GoBack);
            void Stat(VisualElement parent,string label,long amount,float y)
            {Text(parent,label,43,y,437,48,30,Khaki);Text(parent,$"{amount:N0}",449,y,198,48,33,Color.white,TextAnchor.MiddleRight);}
        }
        private void RenderRivalBio()
        {
            var rival=Rival;Header(rival==null?"blacklist":"blacklist "+rival.rank);
            if(rival!=null)
            {
                var background=MostWantedFrontendArt.Find(rival.backgroundTexture);
                if(background!=null)Image(content,background,0,0,Width,Height,null,ScaleMode.ScaleAndCrop);
                AddGrunge(content,-57,0,1070,991,"rival");
                Text(content,"blacklist "+rival.rank,180,37,763,81,63);
                Text(content,rival.fullName+" "+rival.displayName,170,121,844,61,34,Khaki);
                Text(content,"Ride: "+rival.carName,239,188,798,62,37);
                Text(content,"Strength: "+rival.strength,200,253,833,61,35,Khaki);
                var textPanel=Panel(content,171,319,850,555,false,false,new Color(0,0,0,.68f));
                Text(textPanel,"bio:",18,7,280,92,69,Khaki);
                Text(textPanel,rival.biography,12,99,815,379,34);
                var portrait=MostWantedFrontendArt.Find(rival.portraitTexture,"rival-"+rival.id);
                if(portrait!=null)Image(content,portrait,980,51,556,941,null,ScaleMode.ScaleAndCrop);
            }
            else Text(content,"No rival biography is published.",190,330,1150,140,35,Khaki);
            Footer("Back",GoBack);Footer("View Rival Movie",()=>{},"2",Key.None,false);
            Footer("View Rival Car",()=>OpenPage(MostWantedFrontendPage.RivalCar),"3",Key.Digit3,rival!=null);
        }
        private void RenderActivities(bool milestones,bool bountyLocations)
        {
            Header(bountyLocations?"bounty":milestones?"milestones":"race events",true);
            var session=runtime.WorldSession;
            var choices=new List<ActivityChoice>();
            if(session!=null)
            {
                if(bountyLocations)
                {
                    foreach(var location in session.Locations)
                        if(location!=null)choices.Add(new ActivityChoice(location.DisplayName,
                            location.Kind==WorldLocationKind.PoliceStation?"Police station. Set a route to this location and return to free roam.":"Set a route to this location and return to free roam.",
                            location.Kind==WorldLocationKind.PoliceStation?"badge":"car",location.Position,0,false));
                }
                else foreach(var activity in session.Events)
                    if(activity!=null&&(activity.Kind==FreeRoamEventKind.Pursuit)==milestones)
                        choices.Add(new ActivityChoice(activity.DisplayName,$"{activity.Kind} · {activity.Laps} lap(s)\nDrive to the event marker to enter. The existing event service validates eligibility and the starting grid.",
                            activity.Kind==FreeRoamEventKind.Speedtrap?"camera":milestones?"badge":"flag",activity.transform.position,activity.Reward,session.CompletedEvents.Contains(activity.Id)));
            }
            int index=navigation.Select(navigation.Current.Selection,choices.Count);
            var detail=Panel(content,201,115,1167,347,false,false,new Color(0,0,0,.73f));
            var heading=Panel(detail,12,16,1140,123,bountyLocations,false,Color.black);heading.Corners=false;
            if(choices.Count>0)
            {
                var choice=choices[index];Icon(heading,choice.Icon,20,19,70,Amber);
                Text(heading,choice.Name,143,5,952,55,36,Khaki);
                if(!bountyLocations)Text(heading,$"{(milestones?"Bounty":"Cash reward")}:        {choice.Reward:N0}",143,57,931,50,32,Khaki);
                Text(detail,choice.Description,24,154,1111,151,32);
            }
            else
            {
                Text(heading,session==null?"Load a career to view activities":"No matching activities published",26,17,1066,80,34,Khaki);
                Text(detail,"This screen reads authored world content and actual completion records; it does not substitute the reference screenshot's progress.",24,161,1103,130,29);
            }
            var grid=Panel(content,201,473,554,424,false,false,new Color(0,0,0,.52f));
            var stripe=Panel(grid,10,12,532,66,true,false,new Color(0,0,0,.42f));stripe.Corners=false;
            Text(stripe,$"{(choices.Count>0?index+1:0)}/{choices.Count}",329,0,189,61,38,Color.white,TextAnchor.MiddleRight);
            int first=index/9*9;
            for(int i=first;i<Math.Min(first+9,choices.Count);i++)
            {
                int captured=i;int cell=i-first;float x=10+cell%3*179,y=83+cell/3*108;
                var item=Button(grid,string.Empty,()=>{navigation.Select(captured,choices.Count);Render();},"activity-"+i,x,y,173,103,false);
                Icon(item,choices[i].Icon,44,5,83,i==index?Amber:Color.white);
                if(choices[i].Complete)Icon(item,"check",140,0,24,Amber);
                if(i==index)grid.Add(Place(new MostWantedPanel{Fill=Color.clear,CornerColor=Amber},x,y,173,103));
            }
            var roads=session==null?null:FindAnyObjectByType<RoadNetwork>();
            var map=Place(new MostWantedFrontendMap(roads,session!=null?session.transform.position:Vector3.zero,
                choices.Count>0?(Vector3?)choices[index].Position:null),786,472,571,426);content.Add(map);
            horizontalNavigation=delta=>{navigation.Select(index+delta,choices.Count);Render();};
            Footer("Back",GoBack);
            Footer("Set Route",()=>{if(choices.Count>0){session.NavigateTo(choices[index].Position);feedback="Route set. Return to free roam to drive there.";Render();}},"1",Key.Digit1,choices.Count>0);
            Footer("Tips",()=>{feedback="Select an icon, set its route, then return to free roam. Complete the activity to earn its reward.";Render();},"2",Key.Digit2);
        }
        private readonly struct ActivityChoice
        {
            public readonly string Name,Description,Icon;public readonly Vector3 Position;public readonly int Reward;public readonly bool Complete;
            public ActivityChoice(string name,string description,string icon,Vector3 position,int reward,bool complete)
            {Name=name;Description=description;Icon=icon;Position=position;Reward=reward;Complete=complete;}
        }
        private void RenderGarage(bool rivalCar)
        {
            var definition=runtime.WorldSession?.GetComponent<VehicleConfiguration>()?.Definition;
            var identities=new List<string>();
            if(careerSnapshot!=null)
            {
                if(!string.IsNullOrEmpty(careerSnapshot.activeVehicleId))identities.Add(careerSnapshot.activeVehicleId);
                foreach(var id in careerSnapshot.store.ownedVehicleIds)if(!identities.Contains(id))identities.Add(id);
            }
            int index=navigation.Select(navigation.Current.Selection,identities.Count);
            string title=rivalCar?Rival?.carName??"Rival Car":definition!=null?definition.manufacturer+" "+definition.model:"My Cars";
            AddGrunge(content,0,-30,1470,360,"header");
            Text(content,title,367,69,650,93,55);
            Icon(content,"car",247,76,117);
            Text(content,$"{(identities.Count>0?index+1:0)}/{identities.Count}",248,211,166,53,43);
            if(identities.Count>0)Text(content,identities[index],367,167,650,45,26,Khaki);
            var summary=Panel(content,156,774,539,103,false,false,new Color(0,0,0,.43f));summary.Corners=false;
            Text(summary,"Bounty:",9,0,260,48,31,Khaki);Text(summary,$"{Bounty:N0}",270,0,259,48,34,Color.white,TextAnchor.MiddleRight);
            Text(summary,"Fines Due:",9,48,260,48,31,Khaki);Text(summary,$"{runtime.WorldSession?.Pursuit?.CurrentFine??0:N0}",270,48,259,48,34,Color.white,TextAnchor.MiddleRight);
            Text(content,identities.Count==0?"No career vehicle loaded — showroom preview only."
                :"Vehicle selection uses the loaded career vehicle. Additional owned-car swapping is not connected.",177,655,940,66,24,Khaki);
            Footer("Back",GoBack);Footer("Customize",()=>OpenPage(MostWantedFrontendPage.Customize),"1",Key.Digit1,!rivalCar);
            Footer("Showcase",()=>OpenPage(MostWantedFrontendPage.Showcase),"3",Key.Digit3);
            horizontalNavigation=delta=>{navigation.Select(index+delta,identities.Count);Render();};
        }
        private void RenderCustomize()
        {
            Carousel("customize main",new[]{new MenuItem("Visual","wrench",()=>OpenPage(MostWantedFrontendPage.Paint)),
                new MenuItem("Paint","paint",()=>OpenPage(MostWantedFrontendPage.Paint))});
            Footer("Done",GoBack);Footer("Showcase",()=>OpenPage(MostWantedFrontendPage.Showcase),"3",Key.Digit3);
        }
        private VehicleCustomizationDefinition[] PaintChoices()
        {
            if(Customization==null)return Array.Empty<VehicleCustomizationDefinition>();
            return Customization.GetAvailable(VehicleCustomizationCategory.Paint).Where(p=>p!=null).ToArray();
        }
        private void RenderPaint()
        {
            Header("paint");var choices=PaintChoices();int index=navigation.Select(navigation.Current.Selection,choices.Length);
            Text(content,choices.Length>0?choices[index].Style.ToString():"Paint",409,111,343,61,41,Khaki,TextAnchor.MiddleCenter);
            for(int i=0;i<choices.Length;i++)
            {
                int captured=i;float x=184+i%20*33,y=183+(i/20%4)*40;
                if(i/80!=index/80)continue;
                var swatch=Button(content,string.Empty,()=>SelectPaint(choices[captured],captured),"paint-"+i,x,y,28,29,false);
                swatch.style.backgroundColor=choices[i].Visual.Color;
                if(i==index)content.Add(Place(new MostWantedPanel{Fill=Color.clear,CornerColor=Amber},x-5,y-5,38,39));
            }
            var count=Panel(content,181,339,178,50,true,false);count.Corners=false;
            Text(count,$"{(choices.Length>0?index+1:0)}/{choices.Length}",10,0,160,49,39);
            int price=choices.Length>0?choices[index].Price:0;
            var costs=Panel(content,866,62,497,244,true,false,new Color(0,0,0,.51f));
            Text(costs,"Trade in:",13,15,251,46,30,Khaki);Text(costs,"0",269,15,216,46,32,Color.white,TextAnchor.MiddleRight);
            Text(costs,"Cash:",13,61,251,46,30,Khaki);Text(costs,$"{runtime.WorldSession?.Cash??0:N0}",269,61,216,46,32,Color.white,TextAnchor.MiddleRight);
            Text(costs,"Cost:",13,113,251,46,30,Khaki);Text(costs,$"{price:N0}",269,113,216,46,32,Color.white,TextAnchor.MiddleRight);
            Text(costs,"Total:",13,179,251,46,35,Khaki);Text(costs,$"{pendingPaint?.Price??0:N0}",269,179,216,46,35,Color.white,TextAnchor.MiddleRight);
            if(choices.Length==0)Text(content,"No paint catalog is connected to the current vehicle.",185,433,910,100,31,Khaki);
            Footer("Back",GoBack);
            Footer("Add to Cart",()=>{if(choices.Length>0){pendingPaint=choices[index];feedback="1 paint item staged. Checkout is available at a body shop.";Render();}},"↵",Key.Enter,choices.Length>0);
            Footer("View Cart",()=>OpenPage(MostWantedFrontendPage.Cart),"4",Key.Digit4);
            Footer("Showcase",()=>OpenPage(MostWantedFrontendPage.Showcase),"3",Key.Digit3);
            horizontalNavigation=delta=>{int next=navigation.Select(index+delta,choices.Length);if(choices.Length>0)SelectPaint(choices[next],next);};
        }
        private void SelectPaint(VehicleCustomizationDefinition item,int index)
        {
            navigation.Select(index,PaintChoices().Length);
            if(Customization!=null&&Customization.BeginPreview(item,out string failure))showroom?.SetPaintPreview(item.Visual.Color);
            else feedback="Paint preview is unavailable for this vehicle.";
            Render();
        }
        private void RenderCart()
        {
            Header("shopping cart",true);Panel(content,212,195,1118,625);
            if(pendingPaint==null)Text(content,"Your cart is empty.",289,359,961,115,38,Khaki,TextAnchor.MiddleCenter);
            else
            {
                Text(content,pendingPaint.DisplayName,259,326,753,66,35,Khaki);Text(content,$"{pendingPaint.Price:N0}",991,326,281,66,35,Color.white,TextAnchor.MiddleRight);
                Text(content,"1 paint item · Purchase uses the existing shop checkout.\nBrowsing and Back never spend career cash.",260,458,997,117,28);
            }
            bool canBuy=pendingPaint!=null&&OwnsLocation&&runtime.WorldSession.ActiveLocation?.Storefront!=null;
            Footer("Back",GoBack);Footer("Checkout",()=>Confirm("Purchase this paint and install it?",CheckoutPaint),"↵",Key.Enter,canBuy);
            Footer("Remove",()=>{CancelCustomizationPreview();Render();},"1",Key.Digit1,pendingPaint!=null);
            if(pendingPaint!=null&&!canBuy)Text(content,"Enter a body shop to purchase this item.",260,656,998,64,29,Amber);
        }
        private void CheckoutPaint()
        {
            if(pendingPaint==null)return;var selected=pendingPaint;Customization?.CancelPreview();showroom?.ClearPaintPreview();
            if(runtime.WorldSession!=null&&runtime.WorldSession.TryPurchase(selected.ProductId,out string failure))
            {pendingPaint=null;feedback=runtime.WorldSession.Status;RefreshCareerSnapshot();}
            else feedback="Checkout failed. Return to the body shop and check cash, ownership and compatibility.";
            Render();
        }
        private void CancelCustomizationPreview()
        {Customization?.CancelPreview();pendingPaint=null;showroom?.ClearPaintPreview();}
    }

    public sealed class MostWantedFrontendMap : VisualElement
    {
        private readonly RoadNetwork roads;private readonly Vector3 player;private readonly Vector3? target;
        public MostWantedFrontendMap(RoadNetwork network,Vector3 position,Vector3? destination)
        {roads=network;player=position;target=destination;pickingMode=PickingMode.Ignore;generateVisualContent+=Draw;}
        private void Draw(MeshGenerationContext context)
        {
            var p=context.painter2D;float w=contentRect.width,h=contentRect.height;
            MostWantedPanel.Rect(p,0,0,w,h,new Color(.12f,.12f,.12f,.82f));
            if(roads==null||roads.Nodes.Count==0)return;
            float minX=float.MaxValue,minZ=float.MaxValue,maxX=float.MinValue,maxZ=float.MinValue;
            foreach(var n in roads.Nodes){minX=Mathf.Min(minX,n.position.x);maxX=Mathf.Max(maxX,n.position.x);minZ=Mathf.Min(minZ,n.position.z);maxZ=Mathf.Max(maxZ,n.position.z);}
            float scale=Mathf.Min((w-40)/Mathf.Max(1,maxX-minX),(h-40)/Mathf.Max(1,maxZ-minZ));
            Vector2 Map(Vector3 v)=>new Vector2(w*.5f+(v.x-(minX+maxX)*.5f)*scale,h*.5f-(v.z-(minZ+maxZ)*.5f)*scale);
            for(int layer=0;layer<2;layer++)
            {
                p.lineWidth=layer==0?8:4;p.strokeColor=layer==0?Color.black:new Color(.58f,.58f,.58f);
                for(int i=0;i<roads.Nodes.Count;i++)foreach(int next in roads.Nodes[i].exits)
                    if(next>i&&next<roads.Nodes.Count){p.BeginPath();p.MoveTo(Map(roads.Nodes[i].position));p.LineTo(Map(roads.Nodes[next].position));p.Stroke();}
            }
            Vector2 dot=Map(player);p.fillColor=Amber;p.BeginPath();p.MoveTo(dot+new Vector2(0,-15));p.LineTo(dot+new Vector2(12,12));p.LineTo(dot+new Vector2(-12,12));p.ClosePath();p.Fill();
            if(target.HasValue){p.strokeColor=Amber;p.lineWidth=4;p.BeginPath();p.Arc(Map(target.Value),13,0,360);p.Stroke();}
        }
    }
}
