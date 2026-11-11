using UnityEngine;
namespace FlyEditor.Flight
{
    // Provisional balance. Built once per launch or research refresh.
    public sealed class ExtendedResearchStats
    {
        public readonly float air, autoRecovery, boost, bundle, carry, chain, climb, criticalGuard, criticalRepair, damageReduction, disturbance, drag, durability, expressBoost, fastDrag, highClimb, impact, lastMail, launch, log, longDistance, lowDrag, lowGuard, magnetRadius, mail, marks, path, pickupRadius, intervalRepair, record, recoveryTime, response, returnMail, route, section, shield, glide, sort, routeRepair;
        public ExtendedResearchStats(FlightResearch research)
        {
            if (research == null) return;
            if (research.Owns("Web_FingerAlignment")) launch += 0.10f; // 손끝 정렬
            if (research.Owns("Web_WindSlice")) drag += 0.025f; // 바람 가르기
            if (research.Owns("Web_LaunchAlignment")) response += 0.05f; // 발사 각도 보정
            if (research.Owns("Web_LowSprint")) lowDrag += 0.06f; // 저고도 질주
            if (research.Owns("Web_SmoothBody")) drag += 0.03f; // 매끈한 동체
            if (research.Owns("Web_WindBreak")) fastDrag += 0.1f; // 역풍 돌파
            if (research.Owns("Web_StartingSprint")) launch += 0.18f; // 출발선 질주
            if (research.Owns("Web_ContinuousGlide")) carry += 0.25f; // 연속 활강
            if (research.Owns("Web_BoostReserve")) boost += 0.2f; // 추진 여유분
            if (research.Owns("Web_ElasticFlight")) impact += 0.02f; // 탄력 비행
            if (research.Owns("Web_FlightLog")) log += 1f; // 비행 수첩
            if (research.Owns("Web_LevelFlight")) glide += 0.0125f; // 수평 유지
            if (research.Owns("Web_RouteMarks")) marks += 1f; // 항로 눈금
            if (research.Owns("Web_PersonalRecord")) record += 3f; // 개인 기록지
            if (research.Owns("Web_ClimbReserve")) climb += 0.03f; // 상승 여유
            if (research.Owns("Web_GentleDescent")) glide += 0.04f; // Shallower glide slope
            if (research.Owns("Web_GlideTransition")) path += 0.05f; // 활공 전환
            if (research.Owns("Web_RouteQuest")) route += 4f; // 항로 의뢰
            if (research.Owns("Web_RecordStamp")) record += 1f; // 기록 갱신 도장
            if (research.Owns("Web_CloudStep")) highClimb += 0.2f; // 구름턱 넘기
            if (research.Owns("Web_WingTip")) glide += 0.015f; // 날개 끝 조율
            if (research.Owns("Web_LowGlide")) lowDrag += 0.04f; // 낮은 활공
            if (research.Owns("Web_Cruising")) drag += 0.025f; // 순항 자세
            if (research.Owns("Web_RouteSurvey")) marks += 1f; // 항로 측량
            if (research.Owns("Web_SectionRecord")) section += 2f; // 구간 기록
            if (research.Owns("Web_LongCertificate")) longDistance += 3f; // 장거리 인증
            if (research.Owns("Web_DirectionCorrection")) path += 0.05f; // 방향 보정
            if (research.Owns("Web_BalancePoint")) disturbance += 0.05f; // 균형점 찾기
            if (research.Owns("Web_PitchBrake")) glide += 0.03f; // Shallower glide slope
            if (research.Owns("Web_LevelReturn")) recoveryTime += 0.02f; // 수평 복귀
            if (research.Owns("Web_EmergencyAlign")) autoRecovery += 1f; // 비상 재정렬
            if (research.Owns("Web_SteadyHand")) disturbance += 0.06f; // 손떨림 완화
            if (research.Owns("Web_Reclimb")) response += 0.05f; // 빠른 재상승
            if (research.Owns("Web_LowDodge")) lowGuard += 1f; // 저공 회피
            if (research.Owns("Web_NoseStability")) disturbance += 0.06f; // 기수 안정
            if (research.Owns("Web_LandingAttitude")) glide += 0.03f; // Shallower glide slope
            if (research.Owns("Web_CalmRecovery")) recoveryTime += 0.02f; // 침착한 복원
            if (research.Owns("Web_TiltDamping")) disturbance += 0.06f; // 기울기 감쇠
            if (research.Owns("Web_EvasiveGlide")) impact += 0.02f; // 회피 후 활공
            if (research.Owns("Web_ResponseTuning")) response += 0.06f; // 반응 조율
            if (research.Owns("Web_PrecisionControl")) path += 0.07f; // 정밀 조종
            if (research.Owns("Web_MailPickup")) mail += 1f; // 우편 수거
            if (research.Owns("Web_BundleDelivery")) bundle += 2f; // 묶음 배달
            if (research.Owns("Web_AirMail")) air += 2f; // 항공우편
            if (research.Owns("Web_DistantMarker")) magnetRadius += 0.5f; // 먼 표식
            if (research.Owns("Web_RepairTape")) routeRepair += 10f; // 보수 테이프
            if (research.Owns("Web_SortStamp")) sort += 2f; // 분류 도장
            if (research.Owns("Web_HighDelivery")) air += 1f; // 고공 배달
            if (research.Owns("Web_PickupChain")) chain += 3f; // 연속 수거
            if (research.Owns("Web_MailNotice")) pickupRadius += 0.2f; // 우편 예고
            if (research.Owns("Web_RepairKit")) criticalRepair += 15f; // 수선 꾸러미
            if (research.Owns("Web_RegularMail")) mail += 1f; // 정기 우편
            if (research.Owns("Web_RegisteredMail")) sort += 2f; // 등기 우편
            if (research.Owns("Web_ExpressRoute")) expressBoost += 1f; // 항로 특송
            if (research.Owns("Web_ReturnEnvelope")) returnMail += 2f; // 귀환 봉투
            if (research.Owns("Web_LastDelivery")) lastMail += 3f; // 마지막 배달
            if (research.Owns("Web_WingPatch")) durability += 8f; // 날개 덧댐
            if (research.Owns("Web_CushionPaper")) impact += 0.02f; // 완충 겹지
            if (research.Owns("Web_TailReinforce")) durability += 8f; // 꼬리 보강
            if (research.Owns("Web_EmergencyBarrier")) criticalGuard += 1f; // 비상 차폐
            if (research.Owns("Web_ResidualShield")) shield += 1f; // 잔여 보호막
            if (research.Owns("Web_BodyRepair")) intervalRepair += 2f; // 동체 수선
            if (research.Owns("Web_CollisionGlide")) impact += 0.02f; // 충돌 후 활공
            if (research.Owns("Web_ImpactLayer")) damageReduction += 0.05f; // 충격 흡수층
            if (research.Owns("Web_WingSkeleton")) durability += 10f; // 날개 골격
        }
        public int Reward(float distance, float previousBest, int pickups, int highPickups, int longestChain, bool broken)
        {
            float result = (distance >= 20 ? log : 0)
                + Mathf.Min(3, Mathf.Floor(distance / 40)) * marks
                + (distance >= 20 && distance >= previousBest + 10 ? record : 0)
                + (distance >= 80 && pickups >= 2 ? route : 0)
                + (distance >= 60 ? section : 0) + (distance >= 100 ? longDistance : 0)
                + Mathf.Min(3, pickups) * mail + Mathf.Min(2, pickups / 2) * bundle
                + Mathf.Min(2, highPickups) * air + (pickups >= 3 ? sort : 0)
                + (longestChain >= 3 ? chain : 0)
                + (distance >= 20 && pickups >= 1 ? returnMail + (broken ? lastMail : 0) : 0);
            return ResearchEconomy.BonusReward(distance, result);
        }
    }
}