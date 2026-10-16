using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The ordered list of objectives for the tutorial. Objectives appear top to bottom, one after another.
/// Right-click in Project > Create > Tutorial > Mission Sequence.
/// </summary>
[CreateAssetMenu(fileName = "NewTutorialSequence", menuName = "Tutorial/Mission Sequence")]
public class TutorialSequence : ScriptableObject
{
    public List<TutorialMissionData> objectives = new List<TutorialMissionData>();
}