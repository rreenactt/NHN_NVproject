using System;
using NV.Shared.Contracts.Enums;

namespace NV.Client.Net.Session
{
    /// 참가 전 조회 결과. 서버의 `GET /rooms/{code}` 본문을 그대로 옮긴 값이다.
    public readonly struct RoomInfo
    {
        public RoomInfo(
            string code,
            string mapName,
            uint mapHash,
            RoomPhase phase,
            int playerCount,
            int capacity,
            byte hostPlayerId,
            int minPlayers)
        {
            Code = code;
            MapName = mapName;
            MapHash = mapHash;
            Phase = phase;
            PlayerCount = playerCount;
            Capacity = capacity;
            HostPlayerId = hostPlayerId;
            MinPlayers = minPlayers;
        }

        public string Code { get; }

        /// 서버가 로드한 맵의 이름. 어느 씬을 열어야 하는지가 이것으로 갈린다.
        public string MapName { get; }

        public uint MapHash { get; }

        public RoomPhase Phase { get; }

        public int PlayerCount { get; }

        public int Capacity { get; }

        public byte HostPlayerId { get; }

        /// 시작에 필요한 최소 인원. 서버가 정하고 화면은 받아서 표시한다 —
        /// 클라이언트가 따로 적으면 서버 규칙이 바뀔 때 화면만 거짓말을 한다.
        public int MinPlayers { get; }

        public bool IsFull => PlayerCount >= Capacity;
    }

    /// 방 만들기 결과.
    public readonly struct RoomCreateResult
    {
        public RoomCreateResult(string code, string hostToken, string mapName, uint mapHash, int capacity, int minPlayers)
        {
            Code = code;
            HostToken = hostToken;
            MapName = mapName;
            MapHash = mapHash;
            Capacity = capacity;
            MinPlayers = minPlayers;
            Failure = SessionFailureKind.None;
        }

        public RoomCreateResult(SessionFailureKind failure)
        {
            Code = string.Empty;
            HostToken = string.Empty;
            MapName = string.Empty;
            MapHash = 0u;
            Capacity = 0;
            MinPlayers = 0;
            Failure = failure;
        }

        public string Code { get; }

        /// 이 응답에만 실린다. 다시 받아 볼 경로가 없다 — 조회로 얻을 수 있으면
        /// 코드를 아는 누구나 방장이 된다.
        public string HostToken { get; }

        public string MapName { get; }

        public uint MapHash { get; }

        public int Capacity { get; }

        public int MinPlayers { get; }

        public SessionFailureKind Failure { get; }

        public bool Ok => Failure == SessionFailureKind.None;
    }

    /// 참가 전 조회 결과. 방이 있으면 상태를 함께 담는다.
    ///
    /// 실패와 정보가 배타적이지 않다. 정원 초과와 진행 중은 "들어갈 수 없다" 이면서
    /// "방은 있고 상태는 이렇다" 이기도 하다 — 로비가 "8/8 진행 중" 을 표시하려면
    /// 둘이 동시에 필요하다.
    public readonly struct RoomProbeResult
    {
        public RoomProbeResult(RoomInfo info, SessionFailureKind failure, float roundTripSeconds)
        {
            Info = info;
            Failure = failure;
            RoundTripSeconds = roundTripSeconds;
            HasInfo = !string.IsNullOrEmpty(info.Code);
        }

        public RoomInfo Info { get; }

        public bool HasInfo { get; }

        public SessionFailureKind Failure { get; }

        /// 프리플라이트 왕복 시간. 서버가 얼마나 먼지 보여주는 유일한 수치다.
        public float RoundTripSeconds { get; }

        public bool CanJoin => Failure == SessionFailureKind.None;
    }

    /// `JsonUtility` 용 전송 형식.
    ///
    /// `mapHash` 를 long 으로 받는다. 서버는 uint 를 보내며 큰 값은 int 범위를
    /// 넘는데, JsonUtility 의 부호 없는 정수 처리를 신뢰하지 않는 편이 안전하다.
    [Serializable]
    internal sealed class CreateRoomRequestDto
    {
        public string map;
    }

    [Serializable]
    internal sealed class CreateRoomResponseDto
    {
        public string code;
        public string hostToken;
        public string map;
        public string mapName;
        public long mapHash;
        public int capacity;
        public int minPlayers;
    }

    [Serializable]
    internal sealed class RoomInfoResponseDto
    {
        public string code;
        public string mapName;
        public long mapHash;
        public int phase;
        public int playerCount;
        public int capacity;
        public int hostPlayerId;
        public int minPlayers;

        public RoomInfo ToRoomInfo()
        {
            return new RoomInfo(
                code ?? string.Empty,
                mapName ?? string.Empty,
                unchecked((uint)mapHash),
                (RoomPhase)phase,
                playerCount,
                capacity,
                (byte)hostPlayerId,
                minPlayers);
        }
    }

    [Serializable]
    internal sealed class ErrorResponseDto
    {
        public string error;
    }
}
