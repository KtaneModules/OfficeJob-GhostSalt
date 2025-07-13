using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KModkit;
using Rnd = UnityEngine.Random;

public class OfficeJobScript : MonoBehaviour
{
    static int _moduleIdCounter = 1;
    int _moduleID = 0;

    public KMNeedyModule Module;
    public KMSelectable ModuleSelectable;
    public KMBombInfo Bomb;
    public KMAudio Audio;
    public TextMesh[] Labels;
    public SpriteRenderer[] Symbols;
    public Sprite[] SymbolSprites;
    public MeshRenderer[] KeyRends;
    public MeshRenderer Bar;
    public Material[] Mats;

    private Coroutine[] KeyMoveCoroutines = new Coroutine[3];
    private List<KeyCode> CurrentKeys = new List<KeyCode>();
    private List<KeyCode> OffendingKeys = new List<KeyCode>();
    private const int TimeAdded = 6;
    private const int TimeRemoved = 1;
    private const int TimeAddedTP = 20;
    private const int TimeRemovedTP = 0;
    private int PrevCount = 1, SolveCache;
    private bool[] DownKeys = new bool[3];
    private bool Active, Focused, PendingRelease, TPActive;

    private const string KeyboardLayout = "123456789ØQWERTYUIOPASDFGHJKLZXCVBNM";   // Why does Unicode not have a slashed 0 character :sob:

    private int FindActiveSlots()
    {
        if (Bomb.GetSolvableModuleNames().Count() == 0 || Bomb.GetSolvableModuleNames().Count() == SolveCache)
            return 3;   //Just in case
        if (Bomb.GetSolvableModuleNames().Count() == 1)
            return 3;   //Otherwise, division by 0.
        return 1 + Mathf.FloorToInt(2 * (float)SolveCache / (Bomb.GetSolvableModuleNames().Count() - 1));
    }

    private static readonly KeyCode[] TypableKeys =
    {
        KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5, KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9, KeyCode.Alpha0,
        KeyCode.Q, KeyCode.W, KeyCode.E, KeyCode.R, KeyCode.T, KeyCode.Y, KeyCode.U, KeyCode.I, KeyCode.O, KeyCode.P,
        KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.F, KeyCode.G, KeyCode.H, KeyCode.J, KeyCode.K, KeyCode.L,
        KeyCode.Z, KeyCode.X, KeyCode.C, KeyCode.V, KeyCode.B, KeyCode.N, KeyCode.M
    };

    void Awake()
    {
        _moduleID = _moduleIdCounter++;
        for (int i = 0; i < 3; i++)
            SetKeyColour(i, false);
        ModuleSelectable.OnFocus += delegate { HandleFocus(); };
        ModuleSelectable.OnDefocus += delegate { HandleDefocus(); };
        Module.OnNeedyActivation += delegate { HandleActivation(); };
        Module.OnTimerExpired += delegate { HandleTimeExpiry(); };
        Bomb.OnBombSolved += delegate { HandleBombSolved(); };
    }

    // Use this for initialization
    void Start()
    {
        StartCoroutine(SolveCheck());
    }

    // Update is called once per frame
    void Update()
    {
        for (int i = 0; i < TypableKeys.Count(); i++)
        {
            if (Input.GetKeyDown(TypableKeys[i]) && Focused)
                RegisterInput(TypableKeys[i], true);
            else if (Input.GetKeyUp(TypableKeys[i]) && Focused)
                RegisterInput(TypableKeys[i], false);
        }
    }

    void HandleFocus()
    {
        Focused = true;
        var bad = false;
        for (int i = 0; i < TypableKeys.Count(); i++)
            if (Input.GetKey(TypableKeys[i]))
            {
                if (CurrentKeys.Contains(TypableKeys[i]))
                    RegisterInput(TypableKeys[i], true);
                else
                {
                    OffendingKeys.Add(TypableKeys[i]);
                    bad = true;
                }
            }
        if (bad && !PendingRelease)
        {
            Bar.material.color = Color.red;
            Audio.PlaySoundAtTransform("wrong", transform);
        }
    }

    void HandleDefocus()
    {
        Focused = false;
        if (Active)
        {
            for (int i = 0; i < 3; i++)
                if (DownKeys[i])
                    ReleaseIndividual(i);

            if (PendingRelease && OffendingKeys.Count() == 0)
            {
                CurrentKeys = TypableKeys.Where(x => !CurrentKeys.Contains(x)).ToList().Shuffle().Take(FindActiveSlots()).ToList();
                for (int i = 0; i < FindActiveSlots(); i++)
                    SetKeyColour(i, true);
                PendingRelease = false;
            }

            OffendingKeys = new List<KeyCode>();
            Bar.material.color = Color.black;
        }
    }

    void HandleBombSolved()
    {
        Active = false;
        Bar.material.color = Color.black;
        for (int i = 0; i < 3; i++)
            if (DownKeys[i])
                ReleaseIndividual(i);
        for (int i = 0; i < FindActiveSlots(); i++)
            SetKeyColour(i, false);
    }

    void RegisterInput(KeyCode key, bool isDown)
    {
        if (Active)
        {
            if (CurrentKeys.Contains(key))
            {
                var ix = CurrentKeys.IndexOf(key);
                Audio.PlaySoundAtTransform((isDown ? "press " : "release ") + ix, KeyRends[ix].transform);
                if (KeyMoveCoroutines[ix] != null)
                    StopCoroutine(KeyMoveCoroutines[ix]);
                KeyMoveCoroutines[ix] = StartCoroutine(MoveKey(ix, isDown));
                DownKeys[ix] = isDown;
            }
            else
            {
                if (isDown)
                {
                    if (!OffendingKeys.Contains(key))
                    {
                        OffendingKeys.Add(key);
                        Audio.PlaySoundAtTransform("wrong", transform);
                        if (Module.GetNeedyTimeRemaining() > 0.5f)
                            Module.SetNeedyTimeRemaining(Mathf.Max(Module.GetNeedyTimeRemaining() - (TPActive ? TimeRemovedTP : TimeRemoved), 0.5f));
                    }
                }
                else
                    OffendingKeys.Remove(key);
            }
            if (!PendingRelease)
            {
                if (OffendingKeys.Count() > 0)
                    Bar.material.color = Color.red;
                else if (DownKeys.Count(x => x) == CurrentKeys.Count())
                {
                    Module.SetNeedyTimeRemaining(Mathf.Min(Module.GetNeedyTimeRemaining() + (TPActive ? TimeAddedTP : TimeAdded), 99));
                    Bar.material.color = Color.green;
                    Audio.PlaySoundAtTransform("next", transform);
                    PendingRelease = true;
                }
                else
                    Bar.material.color = Color.black;
            }
            else if (DownKeys.Count(x => x) == 0 && OffendingKeys.Count() == 0)
            {
                Bar.material.color = Color.black;
                CurrentKeys = TypableKeys.Where(x => !CurrentKeys.Contains(x)).ToList().Shuffle().Take(FindActiveSlots()).ToList();
                for (int i = 0; i < FindActiveSlots(); i++)
                    SetKeyColour(i, true);
                PendingRelease = false;
            }
        }
    }

    void HandleActivation()
    {
        Active = true;
        CurrentKeys = TypableKeys.ToList().Shuffle().Take(FindActiveSlots()).ToList();
        for (int i = 0; i < FindActiveSlots(); i++)
            SetKeyColour(i, true);
    }

    void HandleTimeExpiry()
    {
        Active = false;
        Bar.material.color = Color.black;
        for (int i = 0; i < 3; i++)
            if (DownKeys[i])
                ReleaseIndividual(i);
        for (int i = 0; i < FindActiveSlots(); i++)
            SetKeyColour(i, false);
        Audio.PlaySoundAtTransform("time up", transform);
        Module.HandleStrike();
        Debug.LogFormat("[Office Job #{0}] Ran out of time — strike!", _moduleID);
    }

    void ReleaseIndividual(int pos)
    {
        Audio.PlaySoundAtTransform("release " + pos, KeyRends[pos].transform);
        if (KeyMoveCoroutines[pos] != null)
            StopCoroutine(KeyMoveCoroutines[pos]);
        KeyMoveCoroutines[pos] = StartCoroutine(MoveKey(pos, false));
        DownKeys[pos] = false;
    }

    void SetKeyColour(int pos, bool isLit)
    {
        KeyRends[pos].material = Mats[isLit ? 1 : 0];
        Symbols[pos].sprite = SymbolSprites[isLit ? 1 : 0];
        Labels[pos].text = isLit ? KeyboardLayout[Array.IndexOf(TypableKeys, CurrentKeys[pos])].ToString() : "";
        if (FindActiveSlots() > PrevCount)
        {
            PrevCount = FindActiveSlots();
            Audio.PlaySoundAtTransform("new key", transform);
        }
    }

    private IEnumerator SolveCheck()
    {
        while (true)
        {
            yield return null;
            if (SolveCache != Bomb.GetSolvedModuleNames().Count())
                SolveCache = Bomb.GetSolvedModuleNames().Count();
        }
    }


    private IEnumerator MoveKey(int pos, bool isDown, float duration = 0.05f, float depression = -0.01f)
    {
        var start = new Vector3(KeyRends[pos].transform.localPosition.x, isDown ? 0 : depression, KeyRends[pos].transform.localPosition.z);
        var end = new Vector3(KeyRends[pos].transform.localPosition.x, isDown ? depression : 0, KeyRends[pos].transform.localPosition.z);
        float timer = 0;
        while (timer < duration)
        {
            yield return null;
            timer += Time.deltaTime;
            KeyRends[pos].transform.localPosition = Vector3.Lerp(start, end, timer / duration);
        }
        KeyRends[pos].transform.localPosition = end;
    }

#pragma warning disable 414
    private string TwitchHelpMessage = "Use \"!{0} ABC\" to input the letters ABC.";
#pragma warning restore 414

    IEnumerator ProcessTwitchCommand(string command)
    {
        TPActive = true;
        command = command.ToUpperInvariant();
        if (command.Length != CurrentKeys.Count())
        {
            yield return "sendtochaterror I need " + CurrentKeys.Count() + " letters!";
            yield break;
        }
        yield return null;
        var keycodes = new List<KeyCode>();
        foreach (var key in command)
        {
            keycodes.Add(TypableKeys[KeyboardLayout.IndexOf(key)]);
            RegisterInput(keycodes.Last(), true);
        }
        yield return null;
        foreach (var keycode in keycodes)
            RegisterInput(keycode, false);
    }
}
