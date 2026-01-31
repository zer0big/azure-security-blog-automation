#!/usr/bin/env python3
"""
Logic App 파라미터 완전 업데이트 (기존 값 유지하면서 rssFeedUrls만 교체)
"""
import json
import subprocess
import sys

RESOURCE_GROUP = "rg-security-blog-automation-dev"

# 5개 보안 피드
FEEDS_SECURITY = [
    {"sourceName": "Microsoft Security Blog", "emoji": "🛡️", "url": "https://www.microsoft.com/en-us/security/blog/feed/"},
    {"sourceName": "Microsoft Sentinel Blog", "emoji": "🔐", "url": "https://techcommunity.microsoft.com/t5/s/gxcuf89792/rss/board?board.id=MicrosoftSentinelBlog"},
    {"sourceName": "Zero Trust Blog", "emoji": "🌐", "url": "https://www.microsoft.com/en-us/security/blog/topic/zero-trust/feed/"},
    {"sourceName": "Threat Intelligence", "emoji": "🎯", "url": "https://www.microsoft.com/en-us/security/blog/topic/threat-intelligence/feed/"},
    {"sourceName": "Cybersecurity Insights", "emoji": "💡", "url": "https://www.microsoft.com/en-us/security/blog/category/cybersecurity/feed/"}
]

# 7개 Azure 피드
FEEDS_AZURE = [
    {"sourceName": "Azure Updates", "emoji": "☁️", "url": "https://azure.microsoft.com/en-us/updates/feed/"},
    {"sourceName": "Azure DevOps Blog", "emoji": "🔧", "url": "https://devblogs.microsoft.com/devops/feed/"},
    {"sourceName": "Azure Architecture Blog", "emoji": "📊", "url": "https://techcommunity.microsoft.com/t5/s/gxcuf89792/rss/board?board.id=AzureArchitectureBlog"},
    {"sourceName": "Azure Infrastructure Blog", "emoji": "🏗️", "url": "https://techcommunity.microsoft.com/t5/s/gxcuf89792/rss/board?board.id=AzureInfrastructureBlog"},
    {"sourceName": "Azure Governance and Management Blog", "emoji": "🏢", "url": "https://techcommunity.microsoft.com/t5/s/gxcuf89792/rss/board?board.id=AzureGovernanceandManagementBlog"},
    {"sourceName": "Azure DevOps Community", "emoji": "🔨", "url": "https://techcommunity.microsoft.com/t5/s/gxcuf89792/rss/board?board.id=AzureDevOpsCommunity"},
    {"sourceName": "Azure Integration Services Blog", "emoji": "⚡", "url": "https://techcommunity.microsoft.com/t5/s/gxcuf89792/rss/board?board.id=IntegrationsonAzureBlog"}
]

def run_az(cmd):
    """az 명령 실행"""
    try:
        result = subprocess.run(
            f"az.cmd {cmd}",
            shell=True,
            capture_output=True,
            text=True,
            encoding='utf-8'
        )
        if result.returncode != 0:
            print(f"❌ {result.stderr}", file=sys.stderr)
            return None
        return result.stdout
    except Exception as e:
        print(f"❌ {e}", file=sys.stderr)
        return None

def update_logic_app(logic_app_name, feeds, schedule_text):
    """Logic App 파라미터 업데이트"""
    print(f"\n📋 {logic_app_name}")
    
    # 1. 현재 파라미터 가져오기
    print("   현재 파라미터 조회...")
    output = run_az(f'logic workflow show --resource-group {RESOURCE_GROUP} --name {logic_app_name} --query "parameters"')
    if not output:
        return False
    
    try:
        current_params = json.loads(output)
    except:
        print("   ❌ JSON 파싱 실패")
        return False
    
    # 2. rssFeedUrls와 scheduleText 교체
    current_params['rssFeedUrls'] = {"value": feeds}
    current_params['scheduleText'] = {"value": schedule_text}
    
    emojis = [f['emoji'] for f in feeds]
    print(f"   ✅ rssFeedUrls 업데이트: {len(feeds)}개 피드")
    print(f"   ✅ 이모지: {emojis}")
    print(f"   ✅ scheduleText: {schedule_text}")
    
    # 3. 파일 저장
    params_file = f"full-params-{logic_app_name}.json"
    with open(params_file, 'w', encoding='utf-8-sig') as f:
        json.dump(current_params, f, ensure_ascii=False, indent=2)
    
    # 4. 업데이트
    print(f"   배포 중...")
    result = run_az(f'logic workflow update --resource-group {RESOURCE_GROUP} --name {logic_app_name} --set parameters=@{params_file}')
    
    if result:
        print(f"   ✅ 성공!")
        return True
    else:
        print(f"   ❌ 실패")
        return False

def main():
    print("=" * 70)
    print("Logic App 런타임 파라미터 완전 업데이트")
    print("=" * 70)
    
    success1 = update_logic_app(
        "logic-dev-security-blog-automation",
        FEEDS_SECURITY,
        "Every day at 07:00, 15:00, 22:00 (KST)"
    )
    
    success2 = update_logic_app(
        "logic-dev-azure-cloud-blog-automation",
        FEEDS_AZURE,
        "Every day at 08:00, 16:00, 23:00 (KST)"
    )
    
    print("\n" + "=" * 70)
    if success1 and success2:
        print("🎉 완료! 런타임 파라미터가 올바르게 업데이트되었습니다!")
        print("\n다음 실행부터:")
        print("  - 올바른 5개 보안 피드 / 7개 Azure 피드 사용")
        print("  - 이모지 정상 표시")
        print("  - 영문 스케줄 텍스트 표시")
    else:
        print("⚠️ 일부 실패")
        sys.exit(1)

if __name__ == "__main__":
    main()
