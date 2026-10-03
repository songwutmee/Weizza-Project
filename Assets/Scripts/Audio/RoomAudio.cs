using UnityEngine;

namespace Weizza
{
    /// <summary>
    /// Changes music and ambience as the player moves between rooms, from the ids
    /// on each Room Definition. An empty id means "keep what is playing", which is
    /// what every room inside one area wants, so a track carries across seamless
    /// borders and only changes at an area edge or a boss.
    /// </summary>
    public static class RoomAudio
    {
        private static string _music;
        private static string _ambience;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _music = null;
            _ambience = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Hook() => WorldStreamer.RoomEntered += OnRoomEntered;

        private static void OnRoomEntered(RoomDefinition room)
        {
            var audio = AudioDirector.Instance;
            if (audio == null || room == null) return;

            if (!string.IsNullOrEmpty(room.musicId) && room.musicId != _music)
            {
                _music = room.musicId;
                audio.PlayBgm(_music, 2f);
            }

            if (!string.IsNullOrEmpty(room.ambienceId) && room.ambienceId != _ambience)
            {
                _ambience = room.ambienceId;
                audio.SetAmbience(_ambience, 1.5f);
            }
        }
    }
}
