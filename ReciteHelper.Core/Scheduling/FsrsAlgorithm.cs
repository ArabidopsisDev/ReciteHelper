namespace ReciteHelper.Core.Scheduling;

/*
 * It's been five or six years since I wrote my first lines of code in 2021. 
 * Looking back, I realize that I've spent most of those years doing something 
 * rather contradictory: taking simple things, making them increasingly complicated, 
 * and then struggling to make them simple again. When I first started programming, 
 * I didn't even know what an interface was. I had never worked with a UI framework. 
 * The best I could manage were tiny programs: a sheep-counting application, 
 * a student management system, and a handful of other tools that now seem almost 
 * embarrassingly primitive. Everything I built was simple. Not because I understood 
 * the value of simplicity, of course. I simply didn't know how to make things complicated.
 *
 * As time went on, I discovered UI frameworks, learned more advanced programming techniques, 
 * and began building software that I hoped people might actually find useful. 
 * But the more I learned, the more I seemed to lose sight of what mattered. 
 * I developed a peculiar obsession: whenever I learned something new, 
 * I wanted to put it into my software. More features, more sophisticated architecture, 
 * more elaborate systems. I wanted people to take one look at my applications and immediately 
 * recognize that they were anything but ordinary.
 *
 * Aim was probably the ultimate expression of that mindset. 
 * Originally, I just wanted to build a task management application, 
 * something similar to Microsoft To Do. At the time, 
 * I didn't even understand what a delegate was in C#. Yet somehow, 
 * I was already determined to build a sophisticated software system. 
 * So I kept adding features.
 *
 * First, I allowed users to create tasks through a command-line interface. 
 * Then I decided that wasn't enough, so I started building an IDE inside the application, 
 * allowing users to write code to automate task creation. At one point, 
 * I even exposed some of the application's low-level signal controllers directly to users. 
 * These were internal mechanisms that should have remained hidden behind layers of abstraction. 
 * Users had absolutely no reason to interact with them. But there they were, 
 * sitting proudly in the interface. Then came online servers, endless configuration options, 
 * and increasingly complicated control logic. All of this, inside an application 
 * that was originally supposed to help people remember what they needed to do.
 *
 * Had I succeeded? In a sense, yes. I had successfully transformed a simple to-do 
 * application into a remarkably complicated system. 
 * The problem was that nobody wanted to use it. Not even me. A crude interface, 
 * incomprehensible interaction patterns, and a collection of technically impressive 
 * but largely unnecessary features had somehow come together to form something almost absurd.
 *
 * Looking back, Aim may not have been a successful application, but it was a remarkably 
 * honest record of my growth. It captured the enthusiasm of a beginner who had just 
 * acquired some technical skills and couldn't resist demonstrating every single one of them. 
 * I used to believe that the complexity of a piece of software reflected the ability 
 * of its developer. Eventually, I began to understand that making software complicated 
 * and making software good are two entirely different things.
 *
 * And that raised a question I could no longer ignore. What kind of software did I actually 
 * want to build? Software that looked sophisticated, powerful, and technologically impressive? 
 * Or software that people genuinely wanted to use?
 *
 * I once read The Three-Body Problem by Liu Cixin. One detail from the novel stayed with me. 
 * In its depiction of a warship's control systems, the higher an operator's rank, 
 * the simpler the interface they use. By the time you reach the captain, 
 * the interface is remarkably minimal. 
 * Looking at it alone, you could hardly imagine that it controls an entire warship. 
 * That idea fascinated me. Why does complexity need to be visible? 
 * Why can't we keep the complicated machinery somewhere users never have to see it, 
 * and give them only what they actually need?
 *
 * A software system might contain thousands of modules, complicated state management, 
 * elaborate scheduling mechanisms, and intricate flows of data. 
 * But from the user's perspective, the goal may be incredibly simple. 
 * They don't care how many APIs are involved. 
 * They don't care how sophisticated the underlying architecture is, 
 * or how many advanced technologies the developer has mastered. 
 * They just want to get something done. Perhaps truly great software isn't software 
 * that constantly reminds users how powerful it is, 
 * but software that never makes them wonder why it works so well.
 *
 * That was the thinking behind ReciteHelper. For the first time, 
 * I began consciously exploring the principles of Calm Technology. 
 * I tried to hide unnecessary details, reduce the number of decisions users had to make, 
 * and allow technology to recede into the background. 
 * Only the things that genuinely mattered should appear on the screen. 
 * But staying true to that philosophy turned out to be much harder than I expected. 
 * Especially in competitions.
 *
 * I once entered ReciteHelper in a software competition.
 * One of the judges was brutally dismissive. 
 * He told me that my project wasn't even as good as Doubao, an AI assistant. 
 * Generating questions and answering them, he argued, were things practically 
 * any AI could already do. And I couldn't entirely disagree. 
 * If you looked only at its list of features, ReciteHelper wasn't particularly impressive. 
 * There was no elaborate multi-agent system to demonstrate, 
 * no spectacular collection of technical buzzwords, 
 * and no attempt to disguise an ordinary feature as some revolutionary breakthrough.
 *
 * For a while, I genuinely wondered whether I had chosen the wrong direction. 
 * I considered adding more advanced technologies. At one point, 
 * I even thought about integrating Emotion-LLaMA to build a multimodal system. 
 * It would certainly have made the project sound more impressive. 
 * The architecture diagram would have looked more sophisticated. 
 * The presentation would have been more exciting. At the very least, 
 * the judges might have seen it as a project with greater technical depth.
 *
 * But then I had to ask myself: Why? Because users actually needed it? 
 * Or because I wanted the judges to think my software was impressive? 
 * Who was I really building this software for? The answer should have been obvious all along. 
 * For its users. And for myself. If a feature genuinely improves the experience, 
 * I don't care how technically difficult it is. 
 * I'll gladly build it. But if the only purpose of a technology is to make 
 * the software look more advanced, does it really deserve to be there?
 *
 * Sticking to Calm Technology meant accepting that I might have to give up some of 
 * the things that tend to win competitions. No spectacular technical demonstrations.
 * No overwhelming feature lists. No artificially manufactured innovations. 
 * Perhaps software designed this way would never make a particularly impressive competition entry. But gradually, something interesting started happening.
 *
 * My friends began using ReciteHelper. As final exams approached, 
 * they started using it more and more frequently. And so did I. 
 * There was something strangely satisfying about that.
 *
 * I had spent countless hours developing Aim, yet I had never genuinely wanted 
 * to use it to manage my own tasks. 
 * Now, I found myself opening ReciteHelper almost without thinking, 
 * using it to study, and gradually forgetting that it was something 
 * I had built myself. It was no longer just a "project" I could show people. 
 * It had become a tool. And suddenly, what the judges thought didn't seem quite so important. 
 * Of course, winning an award is worth celebrating. 
 * But an award shouldn't be the only measure of a piece of software's value. 
 * If people willingly return to an application, 
 * if it becomes a natural part of their everyday lives, 
 * then it has already fulfilled the most fundamental reason I wanted to build it.
 *
 * Later, I began repeatedly introducing another idea in my hackathon presentations: 
 * "Invisible AI." 
 * I wanted artificial intelligence to become something like electricity, 
 * running water, or the internet: an almost invisible part of the systems 
 * we rely on every day. 
 * Users shouldn't have to learn how to use AI just to benefit from it. 
 * They shouldn't be constantly reminded that they're interacting with some remarkably 
 * sophisticated technology. AI should appear when it's needed and quietly disappear when it isn't. It should serve the task, rather than turning the task into a stage on which to demonstrate itself.
 *
 * Ironically, at one of those hackathons, I failed to follow my own philosophy. 
 * We built a project that looked incredibly powerful. It had plenty of technical highlights, 
 * an impressive demonstration, and enough sophistication to earn us third place. 
 * I was genuinely happy about the result.
 *
 * But after the competition, when I looked at the application again, 
 * I realized something rather uncomfortable. I had absolutely no desire to use it. 
 * I knew exactly which technologies it used. I understood its architecture. 
 * I knew how much effort we had invested in making everything work. And yet, 
 * I didn't want to open it. For a moment, I felt as though I were looking at Aim all over again.
 *
 * The difference was that I was no longer the beginner who didn't understand delegates. 
 * I had learned far more about programming, software architecture,
 * and engineering. I could now build systems vastly more sophisticated than anything I had 
 * imagined years earlier. And somehow, I was still making the same mistake. 
 * Knowing what makes software good, it turns out, 
 * doesn't guarantee that you'll build good software. 
 * And resisting the temptation to use a technology may be harder than 
 * learning it in the first place.
 *
 * Over the years, I've traveled from simplicity to complexity, 
 * and then started making my way back toward simplicity. 
 * But the simplicity I'm pursuing now is not the simplicity I started with. 
 * Back then, my software was simple because I lacked the ability to make it complex. 
 * Today, I want my software to be simple because I understand complexity well enough to 
 * choose what matters and what doesn't.
 *
 * I used to want users to see how much work I had done. 
 * Now, I want them not to have to see any of it. I used to take pride in the number of 
 * features I could implement. Now, I find satisfaction in the features I can leave out. 
 * I used to want my software to prove that I was a capable developer. 
 * Now, I want people to forget that there is a developer behind it at all.
 *
 * This doesn't mean I've stopped loving sophisticated technology. Quite the opposite. 
 * I still get excited about elegant software architectures, ingenious algorithms, 
 * and extraordinary feats of engineering. 
 * But I've gradually come to believe that technical complexity should be a burden developers 
 * willingly carry, not one users are forced to bear.
 *
 * Of course, I haven't completely mastered this lesson. 
 * Perhaps someday I'll find myself adding another unnecessary feature. 
 * Perhaps at my next competition, I'll once again hesitate between building 
 * something genuinely useful and building something that looks impressive on stage. 
 * But at least I now know which questions I should keep asking. Whenever I want to add 
 * a feature: Do users actually need it? Whenever I want to introduce a new technology: 
 * What problem does it really solve? Whenever I feel tempted to make a project 
 * appear grander than it needs to be: If there were no competition, no judges, 
 * and no presentation, would I still want to use it myself?
 *
 * Since 2021, I've written simple programs and complicated ones. 
 * Some taught me how to code. Others taught me how to design systems. 
 * And a few taught me to question why I write software in the first place.
 *
 * I used to think that growth meant constantly moving forward: 
 * learning more technologies, mastering more sophisticated techniques, 
 * and building ever-larger systems. Now I'm beginning to realize that 
 * sometimes growth means knowing when to stop. 
 * Sometimes it means carefully removing the very things you once worked so hard to add.
 *
 * So where should I stop? Perhaps the answer isn't simplicity. 
 * And perhaps it isn't complexity, either. 
 * Perhaps it's somewhere in between, at a point where I can finally say: 
 * I know how to build something complex, but I still choose to make it simple. 
 * A place where technology doesn't need to be seen. And software simply needs to be used.
 */

/// <summary>
/// Pure, allocation-free implementation of the FSRS-6 memory model
/// (Difficulty–Stability–Retrievability).  Every function is deterministic and
/// side-effect free; state is carried explicitly by <see cref="MemoryState"/>.
///
/// Notation follows the FSRS reference:
///   R(t, S) = (1 + F·t/S)^(-w20)          with F = 0.9^(-1/w20) - 1, so R(S, S) = 0.9
///   S0(G)   = w[G-1]
///   D0(G)   = w4 - e^{w5 (G-1)} + 1
///   D'      = w7·D0(Easy) + (1 - w7)·(D + ΔD·(10 - D)/9),  ΔD = -w6 (G - 3)
///   S'_rec  = S·(1 + e^{w8}·(11 - D)·S^{-w9}·(e^{w10 (1-R)} - 1)·hard·easy)
///   S'_lap  = min( w11·D^{-w12}·((S+1)^{w13} - 1)·e^{w14 (1-R)},  S / e^{w17 w18} )
///   S'_day  = S·f,  f = e^{w17 (G - 3 + w18)}·S^{-w19}, floored at 1 when G ≥ Hard
/// </summary>
public static class FsrsAlgorithm
{
    public const double MinStability = 0.001;
    public const double MaxStability = 36500.0;
    public const double MinDifficulty = 1.0;
    public const double MaxDifficulty = 10.0;

    /// <summary>Probability of recall after <paramref name="elapsedDays"/> given stability.</summary>
    public static double Retrievability(double stability, double elapsedDays, SchedulerParameters p)
    {
        if (elapsedDays <= 0) return 1.0;
        var s = Math.Max(stability, MinStability);
        return Math.Pow(1.0 + p.Factor * elapsedDays / s, p.Decay);
    }

    /// <summary>
    /// The interval (days) after which retrievability falls to <paramref name="desiredRetention"/>.
    /// Inverse of <see cref="Retrievability"/>: t = S/F · (R^(1/decay) - 1).
    /// </summary>
    public static double IntervalForRetention(double stability, double desiredRetention, SchedulerParameters p)
    {
        if (desiredRetention <= 0 || desiredRetention >= 1)
            throw new ArgumentOutOfRangeException(nameof(desiredRetention), "Desired retention must lie strictly between 0 and 1.");
        var s = Math.Max(stability, MinStability);
        return s / p.Factor * (Math.Pow(desiredRetention, 1.0 / p.Decay) - 1.0);
    }

    /// <summary>Memory state right after the first exposure to an item.</summary>
    public static MemoryState InitialState(ReviewGrade grade, SchedulerParameters p)
    {
        var g = (int)grade;
        if (g is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(grade));

        var stability = Math.Clamp(p[g - 1], MinStability, MaxStability);
        var difficulty = Math.Clamp(InitialDifficulty(g, p), MinDifficulty, MaxDifficulty);
        return new MemoryState(stability, difficulty);
    }

    /// <summary>
    /// Memory state after a review that happened <paramref name="elapsedDays"/> (whole days,
    /// Anki semantics: 0 = same day) after the previous one.
    /// </summary>
    public static MemoryState NextState(MemoryState state, double elapsedDays, ReviewGrade grade, SchedulerParameters p)
    {
        var g = (int)grade;
        if (g is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(grade));
        if (!state.IsValid) throw new ArgumentException("Memory state is outside the admissible ranges.", nameof(state));

        var s = state.Stability;
        var d = state.Difficulty;

        double newStability;
        if (elapsedDays < 1)
        {
            newStability = SameDayStability(s, g, p);
        }
        else
        {
            var r = Retrievability(s, elapsedDays, p);
            newStability = g > 1
                ? StabilityAfterRecall(s, d, r, g, p)
                : StabilityAfterLapse(s, d, r, p);
        }

        var newDifficulty = NextDifficulty(d, g, p);

        return new MemoryState(
            Math.Clamp(newStability, MinStability, MaxStability),
            Math.Clamp(newDifficulty, MinDifficulty, MaxDifficulty));
    }

    internal static double InitialDifficulty(int grade, SchedulerParameters p)
        => p[4] - Math.Exp(p[5] * (grade - 1)) + 1.0;

    internal static double NextDifficulty(double d, int grade, SchedulerParameters p)
    {
        var deltaD = -p[6] * (grade - 3);
        var damped = d + deltaD * (10.0 - d) / 9.0;             // linear damping
        var target = InitialDifficulty((int)ReviewGrade.Easy, p);  // mean-reversion target D0(Easy)
        return p[7] * target + (1.0 - p[7]) * damped;
    }

    internal static double StabilityAfterRecall(double s, double d, double r, int grade, SchedulerParameters p)
    {
        var hardPenalty = grade == (int)ReviewGrade.Hard ? p[15] : 1.0;
        var easyBonus = grade == (int)ReviewGrade.Easy ? p[16] : 1.0;
        return s * (1.0
                    + Math.Exp(p[8])
                    * (11.0 - d)
                    * Math.Pow(s, -p[9])
                    * (Math.Exp((1.0 - r) * p[10]) - 1.0)
                    * hardPenalty
                    * easyBonus);
    }

    internal static double StabilityAfterLapse(double s, double d, double r, SchedulerParameters p)
    {
        var lapse = p[11]
                    * Math.Pow(d, -p[12])
                    * (Math.Pow(s + 1.0, p[13]) - 1.0)
                    * Math.Exp((1.0 - r) * p[14]);
        var ceiling = s / Math.Exp(p[17] * p[18]);
        return Math.Min(lapse, ceiling);
    }

    internal static double SameDayStability(double s, int grade, SchedulerParameters p)
    {
        var sinc = Math.Exp(p[17] * (grade - 3 + p[18])) * Math.Pow(s, -p[19]);
        if (grade >= (int)ReviewGrade.Hard) sinc = Math.Max(sinc, 1.0);
        return s * sinc;
    }
}
