#!/usr/bin/env python3
"""
Correctly update Logic App workflows with 12 feeds using rssFeedUrls parameter.
"""

import subprocess
import json
import sys

def run_command(cmd):
    """Execute PowerShell command and return result"""
    result = subprocess.run(
        ["powershell", "-Command", cmd],
        capture_output=True,
        text=True,
        encoding='utf-8'
    )
    if result.returncode != 0:
        print(f"Error: {result.stderr}", file=sys.stderr)
        sys.exit(1)
    return result.stdout

def get_workflow(resource_group, logic_app_name):
    """Get Logic App workflow from Azure"""
    cmd = f"""
    az rest --method get `
        --uri "/subscriptions/3864b016-4594-40ad-a96b-4a08ac96b537/resourceGroups/{resource_group}/providers/Microsoft.Logic/workflows/{logic_app_name}?api-version=2019-05-01"
    """
    output = run_command(cmd)
    return json.loads(output)

def update_feeds_parameter(workflow, new_feeds, schedule_param_name):
    """Update rssFeedUrls parameter in workflow and fix foreach references"""
    if 'properties' in workflow and 'definition' in workflow['properties']:
        defn = workflow['properties']['definition']
        params = defn.get('parameters', {})
        
        # Update parameter
        if 'rssFeedUrls' in params:
            params['rssFeedUrls']['defaultValue'] = new_feeds
            print(f"   ✅ Updated rssFeedUrls parameter with {len(new_feeds)} feeds")
            
            # Add scheduleText parameter if not exists
            if 'scheduleText' not in params:
                params['scheduleText'] = {
                    "type": "String",
                    "defaultValue": ""
                }
            params['scheduleText']['defaultValue'] = schedule_param_name
            print(f"   ✅ Added/Updated scheduleText parameter")
            
            # Fix foreach reference in For_Each_RSS_Feed action
            actions = defn.get('actions', {})
            if 'For_Each_RSS_Feed' in actions:
                actions['For_Each_RSS_Feed']['foreach'] = "@parameters('rssFeedUrls')"
                print(f"   ✅ Fixed foreach reference to use rssFeedUrls")
                
                # Add emoji field to Append_No_New_Posts
                rss_actions = actions['For_Each_RSS_Feed'].get('actions', {})
                if 'Condition_Has_Items' in rss_actions:
                    has_items_actions = rss_actions['Condition_Has_Items'].get('actions', {})
                    if 'Append_No_New_Posts' in has_items_actions:
                        no_posts_value = has_items_actions['Append_No_New_Posts']['inputs']['value']
                        no_posts_value['emoji'] = "@items('For_Each_RSS_Feed')?['emoji']"
                        print(f"   ✅ Added emoji to Append_No_New_Posts")
                
                # Add emoji field to Append_To_All_Posts
                if 'For_Each_RSS_Item' in rss_actions:
                    item_actions = rss_actions['For_Each_RSS_Item'].get('actions', {})
                    if 'Condition_Is_New' in item_actions:
                        is_new_actions = item_actions['Condition_Is_New'].get('actions', {})
                        if 'Append_To_All_Posts' in is_new_actions:
                            all_posts_value = is_new_actions['Append_To_All_Posts']['inputs']['value']
                            all_posts_value['emoji'] = "@items('For_Each_RSS_Feed')?['emoji']"
                            print(f"   ✅ Added emoji to Append_To_All_Posts")
            
            # Fix scheduleText in Generate_Email_HTML to use parameter
            if 'Generate_Email_HTML' in actions:
                gen_email = actions['Generate_Email_HTML']
                if 'inputs' in gen_email and 'body' in gen_email['inputs']:
                    gen_email['inputs']['body']['scheduleText'] = "@parameters('scheduleText')"
                    print(f"   ✅ Updated Generate_Email_HTML to use scheduleText parameter")
            
            return True
    return False

def deploy_workflow(resource_group, logic_app_name, workflow):
    """Deploy updated workflow to Azure"""
    temp_file = f"{logic_app_name}-updated.json"
    
    # Save with UTF-8 encoding
    with open(temp_file, 'w', encoding='utf-8-sig') as f:
        json.dump(workflow, f, ensure_ascii=False, indent=2)
    
    print(f"📝 Deploying {logic_app_name}...")
    
    cmd = f"""
    $json = Get-Content "{temp_file}" -Raw -Encoding UTF8
    $json | az rest --method put `
        --uri "/subscriptions/3864b016-4594-40ad-a96b-4a08ac96b537/resourceGroups/{resource_group}/providers/Microsoft.Logic/workflows/{logic_app_name}?api-version=2019-05-01" `
        --body "@-" `
        --headers "Content-Type=application/json; charset=utf-8"
    """
    
    try:
        run_command(cmd)
        print(f"✅ Successfully deployed {logic_app_name}")
        return True
    except Exception as e:
        print(f"❌ Failed to deploy {logic_app_name}: {e}")
        return False

def main():
    resource_group = "rg-security-blog-automation-dev"
    
    # Logic App #1: 5 security feeds
    logic_app_1_feeds = [
        {
            "sourceName": "Microsoft Security Blog",
            "emoji": "🛡️",
            "url": "https://www.microsoft.com/en-us/security/blog/feed/"
        },
        {
            "sourceName": "Microsoft Sentinel Blog",
            "emoji": "🔐",
            "url": "https://techcommunity.microsoft.com/t5/s/gxcuf89792/rss/board?board.id=MicrosoftSentinelBlog"
        },
        {
            "sourceName": "Zero Trust Blog",
            "emoji": "🌐",
            "url": "https://www.microsoft.com/en-us/security/blog/topic/zero-trust/feed/"
        },
        {
            "sourceName": "Threat Intelligence",
            "emoji": "🎯",
            "url": "https://www.microsoft.com/en-us/security/blog/topic/threat-intelligence/feed/"
        },
        {
            "sourceName": "Cybersecurity Insights",
            "emoji": "💡",
            "url": "https://www.microsoft.com/en-us/security/blog/category/cybersecurity/feed/"
        }
    ]
    
    # Logic App #2: 7 Azure/Cloud feeds
    logic_app_2_feeds = [
        {
            "sourceName": "Azure Blog",
            "emoji": "☁️",
            "url": "https://azure.microsoft.com/en-us/blog/feed/"
        },
        {
            "sourceName": "Azure DevOps Blog",
            "emoji": "🔧",
            "url": "https://devblogs.microsoft.com/devops/feed/"
        },
        {
            "sourceName": "Fabric Blog",
            "emoji": "📊",
            "url": "https://blog.fabric.microsoft.com/en-us/blog/feed/"
        },
        {
            "sourceName": "Architecture Center",
            "emoji": "🏗️",
            "url": "https://docs.microsoft.com/en-us/azure/architecture/feed.atom"
        },
        {
            "sourceName": "Azure Infrastructure",
            "emoji": "🏢",
            "url": "https://techcommunity.microsoft.com/t5/s/gxcuf89792/rss/board?board.id=AzureInfrastructureBlog"
        },
        {
            "sourceName": "Microsoft 365 Dev",
            "emoji": "🔨",
            "url": "https://devblogs.microsoft.com/microsoft365dev/feed/"
        },
        {
            "sourceName": "Power Platform",
            "emoji": "⚡",
            "url": "https://cloudblogs.microsoft.com/powerplatform/feed/"
        }
    ]
    
    print("="*80)
    print("Logic App Feed Update (Total 12 Feeds)")
    print("="*80)
    
    # Update Logic App #1
    print("\n📋 Logic App #1 (Security - 5 feeds)...")
    try:
        workflow1 = get_workflow(resource_group, "logic-dev-security-blog-automation")
        schedule_text_1 = "Every day at 07:00, 15:00, 22:00 (KST)"
        if update_feeds_parameter(workflow1, logic_app_1_feeds, schedule_text_1):
            deploy_workflow(resource_group, "logic-dev-security-blog-automation", workflow1)
    except Exception as e:
        print(f"❌ Error: {e}")
    
    # Update Logic App #2
    print("\n📋 Logic App #2 (Azure/Cloud - 7 feeds)...")
    try:
        workflow2 = get_workflow(resource_group, "logic-dev-azure-cloud-blog-automation")
        schedule_text_2 = "Every day at 08:00, 16:00, 23:00 (KST)"
        if update_feeds_parameter(workflow2, logic_app_2_feeds, schedule_text_2):
            deploy_workflow(resource_group, "logic-dev-azure-cloud-blog-automation", workflow2)
    except Exception as e:
        print(f"❌ Error: {e}")
    
    print("\n" + "="*80)
    print("✅ 배포 완료!")
    print("="*80)
    print("\n📋 최종 구성 (총 12개 피드):")
    print("\nLogic App #1 (보안 - 5개):")
    for feed in logic_app_1_feeds:
        print(f"   • {feed['sourceName']}")
    print(f"\nLogic App #2 (Azure/Cloud - 7개):")
    for feed in logic_app_2_feeds:
        print(f"   • {feed['sourceName']}")

if __name__ == "__main__":
    main()
