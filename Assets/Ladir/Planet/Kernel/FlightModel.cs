using System;
using static Ladir.Planet.PM;

namespace Ladir.Planet
{
    public sealed class FlightModel
    {
        public const double Cruise=700/3.6,Boost=1800/3.6,Clearance=17.2;
        public static readonly double[] WarpDistances={1000000,3000000,6000000,9000000,12000000};
        public readonly TerrainKernel terrain;
        public D3 position,previousPosition,forward,previousForward,heading,up,right,velocity,spawn,landingDirection;
        public double speed=Cruise,pitch,bank,roll,rollRemaining,rollSign=1,health=100,boostEnergy=100,fallSpeed,altitude,agl,time,lockTime,hit,damageFlash;
        public bool paused,cruising=true,boosting,boostExhausted,freeFalling;
        public string state="flying",warpPhase="idle";
        public double warpCharge,warpCooldown,warpElapsed,warpDuration,warpTravel,warpProgress,repairRemaining,repairCooldown,landingHeight;
        public int warpIndex=4,target=0,score,tick,shots;
        public D3 warpFrom,warpForward,warpTo;double warpEntry,gunTimer,missileTimer;
        public FlightInput input;
        public readonly Enemy[] enemies=new Enemy[6];
        public readonly Projectile[] projectiles=new Projectile[128];
        public readonly Explosion[] explosions=new Explosion[16];
        public Action<string> Message;
        public Action<bool> Shot;
        public struct FlightInput{public double x,y,strafe;public bool boost,brake,gun,missile;}
        public class Enemy {public int id;public double health=100,speed,phase,pursuit,lost,cooldown,fire=2,respawn,ground;public D3 position,previous,home,forward,velocity;public string mode="patrol";public bool visible;}
        public class Projectile {public D3 position,previous,direction;public double life,speed,damage;public int target;public bool enemy,missile;}
        public class Explosion {public D3 position;public double life,duration;public bool impact;}
        double Radius=>terrain.World.radiusMeters;
        public FlightModel(TerrainKernel kernel,D3 direction,D3 initialHeading)
        {
            terrain=kernel;spawn=direction.Unit;up=spawn;heading=(initialHeading-up*D3.Dot(initialHeading,up)).Unit;forward=heading;right=D3.Cross(forward,up).Unit;position=up*(Radius+Ground(up)+320);previousPosition=position;previousForward=forward;velocity=forward*speed;
            for(int i=0;i<projectiles.Length;i++)projectiles[i]=new Projectile();for(int i=0;i<explosions.Length;i++)explosions[i]=new Explosion();for(int i=0;i<enemies.Length;i++)enemies[i]=CreateEnemy(i);RefreshGround();
        }
        public double Ground(D3 d){var s=terrain.Sample(d);return Math.Max(s.height,s.water);}
        public static D3 North(D3 up){var north=new D3(0,1,0)-up*up.y;if(north.Length<.0001)north=new D3(0,0,1)-up*up.z;return north.Unit;}
        static D3 Rotate(D3 d,D3 axis,double angle)=>d*Math.Cos(angle)+D3.Cross(axis,d)*Math.Sin(angle)+axis*D3.Dot(axis,d)*(1-Math.Cos(angle));
        static double Damp(double x,double to,double rate,double dt)=>Mix(x,to,1-Math.Exp(-dt*rate));
        void RefreshGround(){up=position.Unit;altitude=position.Length-Radius;agl=altitude-(altitude<60000?Ground(up):0);}
        public bool Activate(int slot)
        {
            if(paused||health<=0)return false;
            if(slot==1&&state=="flying"&&warpCharge==0&&warpCooldown==0){warpCharge=3;warpPhase="charging";Message?.Invoke("Warp · tekerlek ile mesafe seç · 3 saniye sonra hareket");return true;}
            if(slot==2&&health<100&&repairRemaining==0&&repairCooldown==0){repairRemaining=5;repairCooldown=30;Message?.Invoke("5 saniyede %50 onarım · 30 sn bekleme");return true;}return false;
        }
        public void SelectWarp(int index){if(warpCharge<=0)return;warpIndex=(int)Clamp(index,0,WarpDistances.Length-1);warpCharge=3;}
        public void CycleTarget(){for(int i=1;i<=enemies.Length;i++){int next=(Math.Max(-1,target)+i)%enemies.Length;if(enemies[next].health>0){target=next;lockTime=0;return;}}target=-1;}
        public void Roll(int sign){if(state=="flying"&&rollRemaining==0){rollRemaining=.85;rollSign=sign;}}
        public bool Landing()
        {
            if(state=="landed"){state="taking-off";cruising=true;return true;}if(state=="landing"){state="flying";cruising=false;return true;}
            if(state!="flying"||altitude>=60000)return false;
            D3 north=North(up),east=D3.Cross(north,up).Unit;
            foreach(double r in new double[]{0,80,200,450,800,1200})for(int i=0;i<(r==0?1:12);i++)
            {double angle=i*Math.PI/6;D3 d=(up+(north*(Math.Cos(angle)*r)+east*(Math.Sin(angle)*r))/Radius).Unit;if(LandingSite(d,out double h)){landingDirection=d;landingHeight=h;state="landing";cruising=false;pitch=0;Message?.Invoke("İniş yardımı · C ile iptal");return true;}}
            Message?.Invoke("Yakında düz, kuru iniş alanı yok.");return false;
        }
        bool LandingSite(D3 d,out double height)
        {
            D3 n=North(d),e=D3.Cross(n,d).Unit;var center=terrain.Sample(d);double lo=center.height,hi=lo;bool valid=center.Depth<.5&&center.forest<.68;
            for(int i=0;i<4;i++){var s=terrain.Sample((d+(n*(i<2?-45:45)+e*(i%2==0?-45:45))/Radius).Unit);lo=Math.Min(lo,s.height);hi=Math.Max(hi,s.height);valid&=s.Depth<.5&&s.forest<.68;}height=hi;return valid&&Math.Atan((hi-lo)/90)<.25;
        }
        public static (double distance,double speed,string phase) WarpMotion(double elapsed,double duration,double distance,double entry,double exit=Cruise)
        {
            double t=Clamp(elapsed,0,duration),a=duration/6,b=duration/3,brake=duration-b,peak=(distance-entry*a/2-exit*b/2)/(duration-(a+b)/2),travel,speed;string phase;
            if(t<a){double u=t/a;travel=entry*t+(peak-entry)*a*u*u*u*(1-u/2);speed=Mix(entry,peak,u*u*(3-2*u));phase="accelerating";}
            else if(t<brake){travel=a*(entry+peak)/2+peak*(t-a);speed=peak;phase="cruising";}
            else{double local=t-brake,u=local/b;travel=a*(entry+peak)/2+peak*(brake-a)+peak*local+(exit-peak)*b*u*u*u*(1-u/2);speed=Mix(peak,exit,u*u*(3-2*u));phase="decelerating";}
            return(t==duration?distance:travel,speed,phase);
        }
        void BeginWarp()
        {
            warpFrom=position;warpForward=forward.Unit;warpTo=position+warpForward*WarpDistances[warpIndex];warpEntry=speed;warpDuration=Math.Max(3,12*WarpDistances[warpIndex]/12000000);warpElapsed=warpTravel=warpProgress=0;warpCharge=0;warpPhase="accelerating";state="warping";boosting=false;bank=roll=fallSpeed=0;
            foreach(var e in enemies){e.mode="return";e.cooldown=18;}foreach(var p in projectiles)p.life=0;
        }
        void FinishWarp(bool impact)
        {state="flying";warpPhase="idle";warpCooldown=20;warpCharge=0;fallSpeed=0;RefreshGround();pitch=Math.Asin(Clamp(D3.Dot(forward,up),-1,1));heading=(forward-up*D3.Dot(forward,up)).Unit;if(heading.Length<.5)heading=North(up);if(!impact)speed=Cruise;Message?.Invoke(impact?"Warp kesildi · Zeminle çarpışma":"Warp tamamlandı · 20 saniye bekleme");}
        public bool Sweep(D3 from,D3 to,out D3 point,out D3 normal)
        {
            point=to;normal=to.Unit;D3 ray=to-from;double length=ray.Length;if(length==0)return false;ray/=length;double b=D3.Dot(from,ray),outer=Radius+60000,disc=b*b-D3.Dot(from,from)+outer*outer;if(disc<0)return false;
            double near=Math.Max(0,-b-Math.Sqrt(disc)),far=Math.Min(length,-b+Math.Sqrt(disc));if(near>far)return false;
            double Gap(double travel){D3 probe=from+ray*travel;return probe.Length-Radius-Ground(probe.Unit)-Clearance;}
            double previous=near,at=near,gap=Gap(near);
            while(true)
            {
                if(gap<=0){double lo=previous,hi=at;for(int i=0;i<15&&hi-lo>.005;i++){double mid=(lo+hi)/2;if(Gap(mid)>0)lo=mid;else hi=mid;}D3 d=(from+ray*hi).Unit;point=d*(Radius+Ground(d)+Clearance);normal=terrain.Normal(d,8);return true;}
                if(at>=far)return false;previous=at;at=Math.Min(far,at+Math.Max(25,Math.Min(125,gap*.25)));gap=Gap(at);
            }
        }
        void Impact(D3 point,D3 normal)
        {double energy=Math.Max(0,-D3.Dot(velocity,normal));position=point;fallSpeed=0;pitch=.18;velocity=(velocity+normal*energy*1.15)*.4;speed=velocity.Length;Explode(point,true);Hurt(energy>75?100:Math.Max(2,energy*.8));}
        public void Hurt(double amount)
        {if(health<=0)return;health=Math.Max(0,health-amount);damageFlash=.45;if(health==0){state="crashed";speed=0;velocity=new D3();warpCharge=0;warpPhase="idle";Explode(position,false);Message?.Invoke("Gemi devre dışı · Yeniden uç");}}
        public void Step(double dt)
        {
            if(paused)return;previousPosition=position;previousForward=forward;time+=dt;tick++;
            gunTimer=Math.Max(0,gunTimer-dt);missileTimer=Math.Max(0,missileTimer-dt);hit=Math.Max(0,hit-dt);damageFlash=Math.Max(0,damageFlash-dt);warpCooldown=Math.Max(0,warpCooldown-dt);repairCooldown=Math.Max(0,repairCooldown-dt);
            if(health>0&&repairRemaining>0){health=Math.Min(100,health+Math.Min(dt,repairRemaining)*10);repairRemaining=Math.Max(0,repairRemaining-dt);}
            if(state=="warping")
            {
                warpElapsed=Math.Min(warpDuration,warpElapsed+dt);var motion=WarpMotion(warpElapsed,warpDuration,WarpDistances[warpIndex],warpEntry);D3 to=warpFrom+warpForward*motion.distance;speed=motion.speed;velocity=warpForward*speed;forward=warpForward;
                if(Sweep(position,to,out var point,out var normal)){position=point;FinishWarp(true);Impact(point,normal);return;}
                position=to;warpTravel=motion.distance;warpProgress=warpElapsed/warpDuration;warpPhase=motion.phase;RefreshGround();if(warpElapsed>=warpDuration)FinishWarp(false);return;
            }
            D3 beforeUp=up;RefreshGround();var axis=D3.Cross(beforeUp,up);if(axis.Length>1e-10)heading=Rotate(heading,axis.Unit,Math.Atan2(axis.Length,D3.Dot(beforeUp,up)));heading=(heading-up*D3.Dot(heading,up)).Unit;
            if(boostEnergy<=1)boostExhausted=true;if(!input.boost&&boostEnergy>=12)boostExhausted=false;boosting=state=="flying"&&input.boost&&!boostExhausted&&boostEnergy>1;boostEnergy=Clamp(boostEnergy+dt*(boosting?-21:12),0,100);
            if(state=="flying")
            {
                bool charging=warpCharge>0;double x=charging?0:Clamp(input.x,-1,1),y=charging?0:Clamp(input.y,-1,1);heading=Rotate(heading,up,-x*1.8*dt);pitch=Clamp(pitch-y*1.3*dt,-1.55,1.55);
                double desired=input.brake?32:boosting?Boost:cruising?Cruise:0,drive=Math.Max(0,speed-fallSpeed),down=Math.Max(0,-Math.Sin(pitch));freeFalling=down>.035&&!input.brake;
                if(freeFalling){double gravity=9.81*Sq(Radius/Math.Max(Radius,position.Length)),drag=.00006*Math.Exp(-Math.Max(0,altitude)/18000);fallSpeed=(fallSpeed+gravity*down*dt)/(1+drag*fallSpeed*dt);}else fallSpeed=Damp(fallSpeed,0,input.brake||!cruising?2.8:1.1,dt);
                speed=Damp(drive,desired,input.brake||!cruising?2.8:1.8,dt)+fallSpeed;forward=(heading*Math.Cos(pitch)+up*Math.Sin(pitch)).Unit;right=D3.Cross(forward,up).Unit;velocity=forward*speed+right*(charging?0:input.strafe*65);
                D3 to=position+velocity*dt;bank=Damp(bank,-x*.48-input.strafe*.32,6,dt);if(rollRemaining>0){rollRemaining=Math.Max(0,rollRemaining-dt);roll=rollSign*Math.PI*2*Smooth(0,1,1-rollRemaining/.85);}else roll=0;
                if(Sweep(position,to,out var contact,out var normal))Impact(contact,normal);else position=to;
            }
            else if(state=="landing"||state=="taking-off")
            {
                bank=Damp(bank,0,8,dt);roll=0;speed=Damp(speed,0,4,dt);forward=heading;velocity=new D3();fallSpeed=0;
                if(state=="taking-off"){position+=up*Math.Min(42,14+agl*.45)*dt;if(agl>125){state="flying";speed=45;}}
                else{var offset=landingDirection*position.Length-position;double distance=offset.Length;if(distance>1)position+=offset*(Math.Min(distance,75*dt)/distance);D3 d=position.Unit;double g=Ground(d),desired=Radius+(distance>5?Math.Max(g,landingHeight)+Math.Max(55,Math.Min(180,distance*.25)):landingHeight+Clearance),current=position.Length,step=Clamp(desired-current,-Math.Min(55,Math.Max(3,(current-desired)*.5))*dt,80*dt);position=d*Math.Max(Radius+g+Clearance,current+step);if(distance<1.5&&Math.Abs(position.Length-desired)<.3){position=landingDirection*desired;state="landed";speed=0;}}
            }
            else if(state=="landed"){if(repairRemaining==0)health=Math.Min(100,health+dt*8);boostEnergy=100;velocity=new D3();}
            RefreshGround();if(target>=0){D3 delta=enemies[target].position-position;lockTime=delta.Length<2600&&D3.Dot(delta.Unit,forward)>.84?Math.Min(1,lockTime+dt):0;}
            if(input.gun)Shoot(false);if(input.missile)Shoot(true);UpdateEnemies(dt);UpdateProjectiles(dt);foreach(var e in explosions)e.life=Math.Max(0,e.life-dt);
            if(warpCharge>0){if(state!="flying"){warpCharge=0;warpPhase="idle";}else{warpCharge=Math.Max(0,warpCharge-dt);if(warpCharge<1e-8)BeginWarp();}}
        }
        Enemy CreateEnemy(int id)
        {double angle=(id-2.5)*.22,dist=650+id*330;D3 d=(up+(heading*Math.Cos(angle)+right*Math.Sin(angle))*dist/Radius).Unit,p=d*(Radius+Math.Max(Ground(d)+250,position.Length-Radius+(id%3-1)*65));return new Enemy{id=id,position=p,previous=p,home=p,forward=heading,speed=150+id*7,ground=Ground(d),phase=id*1.7,fire=2+id*.75};}
        void SpawnProjectile(D3 p,D3 d,bool missile,bool enemy,int target,double damage,double speed,double life)
        {foreach(var shot in projectiles)if(shot.life<=0){shot.position=shot.previous=p;shot.direction=d;shot.missile=missile;shot.enemy=enemy;shot.target=target;shot.damage=damage;shot.speed=speed;shot.life=life;return;}}
        void Shoot(bool missile)
        {
            if(state!="flying"||health<=0||(missile?missileTimer:gunTimer)>0)return;if(missile&&(target<0||lockTime<.8))return;D3 direction=forward;
            if(target>=0&&enemies[target].health>0){var e=enemies[target];var delta=e.position-position;if(missile||D3.Dot(delta.Unit,direction)>.992)direction=(e.position+e.velocity*(delta.Length/1350)-position).Unit;}
            SpawnProjectile(position+forward*52.8+right*(shots%2==0?-17:17),direction,missile,false,missile?target:-1,missile?62:14,missile?620:1350,missile?5:1.7);if(missile)missileTimer=1.4;else{gunTimer=.095;shots++;}Shot?.Invoke(missile);
        }
        void UpdateEnemies(double dt)
        {
            foreach(var e in enemies)
            {
                if(e.health<=0){e.respawn-=dt;if(e.respawn<=0){e.health=100;e.position=e.previous=e.home;e.mode="patrol";}continue;}e.previous=e.position;D3 up=e.position.Unit,delta=position-e.position,north=North(e.home.Unit),side=D3.Cross(north,e.home.Unit).Unit;double distance=delta.Length;e.cooldown=Math.Max(0,e.cooldown-dt);
                if(tick%36==e.id*6){e.visible=distance<3200&&state=="flying"&&health>0;if(e.visible)for(int j=1;j<=3;j++){var q=e.position+delta*(j*.25);if(q.Length<Radius+Ground(q.Unit)+12){e.visible=false;break;}}}
                if(e.mode=="patrol"&&e.cooldown==0&&distance<1800&&e.visible){e.mode="pursue";e.pursuit=e.lost=0;}
                if(e.mode=="pursue"){e.pursuit+=dt;e.lost=e.visible?0:e.lost+dt;if(e.pursuit>=22||distance>3200||(e.position-e.home).Length>4500||e.lost>4||state!="flying"){e.mode="return";e.cooldown=18;e.fire=Math.Max(2,e.fire);}}
                if(e.mode=="return"&&(e.position-e.home).Length<450){e.mode="patrol";e.cooldown=Math.Max(18,e.cooldown);}
                D3 goal=e.mode=="pursue"?position+heading*(distance<230?850:350)+side*Math.Sin(time*.22+e.phase)*400:e.mode=="return"?e.home:e.home+north*Math.Cos(time*.10+e.phase)*650+side*Math.Sin(time*.10+e.phase)*650;
                e.forward=(e.forward+((goal-e.position).Unit-e.forward)*(1-Math.Exp(-dt*.65))).Unit;
                if(tick%12==e.id*2)e.ground=Math.Max(Ground(up),Ground((e.position+e.forward*e.speed*2).Unit));
                double wanted=Math.Max(e.ground+160,(e.mode=="pursue"?altitude:e.home.Length-Radius)+Math.Sin(time*.15+e.phase)*100),vertical=Clamp((wanted-(e.position.Length-Radius))*.6,-75,110);
                e.forward=(e.forward-up*D3.Dot(e.forward,up)).Unit;e.velocity=e.forward*e.speed+up*vertical;e.position+=e.velocity*dt;if(tick%12==e.id*2){double floor=Radius+Ground(e.position.Unit)+40;if(e.position.Length<floor)e.position=e.position.Unit*floor;}
                e.fire-=dt;if(e.mode=="pursue"&&e.visible&&time>12&&state=="flying"&&distance<1250&&distance>140&&e.fire<=0&&D3.Dot(e.forward,delta.Unit)>.45){e.fire=1.9+e.id*.15;D3 aim=position+velocity*(distance/720*.75)+side*Math.Sin(time+e.phase)*28;SpawnProjectile(e.position,(aim-e.position).Unit,false,true,-1,5,720,2.2);}
            }
        }
        static double SegmentDistance(D3 point,D3 a,D3 b){D3 v=b-a;double t=Clamp(D3.Dot(point-a,v)/Math.Max(1e-9,D3.Dot(v,v)));return(point-a-v*t).Length;}
        void UpdateProjectiles(double dt)
        {
            for(int i=0;i<projectiles.Length;i++)
            {
                var p=projectiles[i];if(p.life<=0)continue;p.previous=p.position;p.life-=dt;
                if(p.missile&&p.target>=0&&enemies[p.target].health>0){var e=enemies[p.target];p.direction=(p.direction+((e.position+e.velocity*.2-p.position).Unit-p.direction)*(1-Math.Exp(-dt*3.5))).Unit;}p.position+=p.direction*p.speed*dt;
                if(p.enemy){if(state=="flying"&&SegmentDistance(position,p.previous,p.position)<40){Hurt(p.damage);p.life=0;}}
                else foreach(var e in enemies)if(e.health>0&&SegmentDistance(e.position,p.previous,p.position)<(p.missile?55:44)){e.health=Math.Max(0,e.health-p.damage);p.life=0;hit=.2;Explode(p.position,true);if(e.health==0){e.respawn=18;score++;Explode(e.position,false);if(target==e.id)CycleTarget();}break;}
                if(p.life>0&&tick%4==i%4&&p.position.Length<Radius+Ground(p.position.Unit)){p.life=0;Explode(p.position,true);}
            }
        }
        void Explode(D3 p,bool impact){Explosion free=explosions[0];foreach(var e in explosions)if(e.life<=0){free=e;break;}free.position=p;free.impact=impact;free.duration=impact?.28:1.4;free.life=free.duration;}
    }
}
