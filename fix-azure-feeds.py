#!/usr/bin/env python3
"""
Azure Logic App 피드 수정 (Azure Updates 제거)
"""
import json
import subprocess
import sys

RESOURCE_GROUP = "rg-security-blog-automation-dev"

# Azure Updates 제거, 나머지 6개만 사용
FEEDS_AZURE = [
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

def main():
    logic_app_name = "logic-dev-azure-cloud-blog-automation"
    
    print("=" * 70)
    print(f"🔧 {logic_app_name}")
    print("=" * 70)
    print(f"\n⚠️ Azure Updates 피드 제거 (HTML 반환으로 인한 오류)")
    print(f"✅ 6개 유효한 피드로 업데이트\n")
    
    # 1. 현재 파라미터 가져오기
    print("📥 현재 파라미터 조회...")
    output = run_az(f'logic workflow show --resource-group {RESOURCE_GROUP} --name {logic_app_name} --query "parameters"')
    if not output:
        sys.exit(1)
    
    try:
        current_params = json.loads(output)
    except:
        print("❌ JSON 파싱 실패")
        sys.exit(1)
    
    # 2. rssFeedUrls 교체
    current_params['rssFeedUrls'] = {"value": FEEDS_AZURE}
    
    emojis = [f['emoji'] for f in FEEDS_AZURE]
    print(f"✅ rssFeedUrls 업데이트: {len(FEEDS_AZURE)}개 피드")
    print(f"✅ 이모지: {emojis}")
    
    # 3. 파일 저장
    params_file = f"full-params-{logic_app_name}.json"
    with open(params_file, 'w', encoding='utf-8-sig') as f:
        json.dump(current_params, f, ensure_ascii=False, indent=2)
    
    # 4. 업데이트
    print(f"\n📝 배포 중...")
    result = run_az(f'logic workflow update --resource-group {RESOURCE_GROUP} --name {logic_app_name} --set parameters=@{params_file}')
    
    if result:
        print(f"\n🎉 완료!")
        print(f"\n사용 피드:")
        for feed in FEEDS_AZURE:
            print(f"  {feed['emoji']} {feed['sourceName']}")
    else:
        print(f"\n❌ 실패")
        sys.exit(1)

if __name__ == "__main__":
    main()
