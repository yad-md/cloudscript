cloudscript wordpress_single_stack
    version                     = _latest  
    result_template             = wordpress_result_template

globals
    wp_image_type               = 'Ubuntu Server 14.04 LTS'
    wp_instance_type            = 'CS1'
    wp_hostname                 = 'wordpress'
    server_password	        = lib::random_password()
    console_password            = lib::random_password()
    mysql_root_password         = lib::random_password()
    wordpress_admin_password    = lib::random_password()
    wordpress_slice_user        = 'wordpress'
    wordpress_db_username       = 'wordpress'
    wordpress_db_name           = 'wordpress'
    wordpress_db_password       = lib::random_password()
    wordpress_auth_key          = lib::random_password()
    wordpress_secure_auth_key   = lib::random_password()
    wordpress_logged_in_key     = lib::random_password()
    wordpress_nonce_key         = lib::random_password()
    wordpress_auth_salt         = lib::random_password()
    wordpress_secure_auth_salt  = lib::random_password()
    wordpress_logged_in_salt    = lib::random_password()
    wordpress_nonce_salt        = lib::random_password()
    
thread wordpress_setup
    tasks                       = [wordpress_server_setup]

task wordpress_server_setup

    #-------------------------------
    # create wordpress keys
    #-------------------------------
    
    # create wordpress server root password key
    /key/password wordpress_server_password_key read_or_create
        key_group               = _SERVER
        password                = server_password
    
    # create wordpress server console key
    /key/password wordpress_server_console_key read_or_create
        key_group               = _CONSOLE
        password                = console_password
        
    # create storage slice keys
    /key/token wordpress_slice_key read_or_create
        username                = wordpress_slice_user
        
    #-------------------------------
    # create wordpress bootstrap 
    # script and recipe
    #-------------------------------
    
    # create slice to store script in cloudstorage
    /storage/slice wordpress_slice read_or_create
        keys                    = [wordpress_slice_key]
    
    # create slice container to store script in cloudstorage
    /storage/container wordpress_container => [wordpress_slice] read_or_create
        slice                   = wordpress_slice
    
    # place script data in cloudstorage
    /storage/object wordpress_bootstrap_object => [wordpress_slice, wordpress_container] read_or_create
        container_name          = 'wordpress_container'
        file_name               = 'bootstrap_wordpress.sh'
        slice                   = wordpress_slice
        content_data            = wordpress_bootstrap_data
        
    # associate the cloudstorage object with the wordpress script
    /orchestration/script wordpress_bootstrap_script => [wordpress_slice, wordpress_container, wordpress_bootstrap_object] read_or_create
        data_uri                = 'cloudstorage://wordpress_slice/wordpress_container/bootstrap_wordpress.sh'
        script_type             = _SHELL
        encoding                = _STORAGE
    
    # create the recipe and associate the script
    /orchestration/recipe wordpress_bootstrap_recipe read_or_create
        scripts                 = [wordpress_bootstrap_script]

    #-------------------------------
    # create the wordpress server
    #-------------------------------
    
    /server/cloud wordpress_server read_or_create
        hostname                = '{{ wp_hostname }}'
        image                   = '{{ wp_image_type }}'
        service_type            = '{{ wp_instance_type }}'
        keys                    = [wordpress_server_password_key, wordpress_server_console_key]
        recipes                 = [wordpress_bootstrap_recipe]

text_template wordpress_bootstrap_data
#!/bin/sh

#-------------------------
# Install packages
#-------------------------

# get latest package list
apt-get update

# install apache2
apt-get install -y apache2

# let the user know wordpress is coming
echo "Wordpress is installing, please wait..." > /var/www/index.html

# prepare mysql preseed
MYSQL_ROOT_PWD={{ mysql_root_password }}
echo "mysql-server-5.1 mysql-server/root_password password $MYSQL_ROOT_PWD" > mysql.preseed
echo "mysql-server-5.1 mysql-server/root_password_again password $MYSQL_ROOT_PWD" >> mysql.preseed
echo "mysql-server-5.1 mysql-server/start_on_boot boolean true" >> mysql.preseed
cat mysql.preseed | sudo debconf-set-selections
rm mysql.preseed

# install php/mysql packages
apt-get -y install mysql-server
apt-get -y install libapache2-mod-php5
apt-get -y install php5-mysql

# Change apache config & restart apache to use the modules
sed -r 's@DocumentRoot /var/www/html@DocumentRoot /var/www@g'/etc/apache2/sites-available/000-default.conf > /etc/apache2/sites-available/111-default.conf
mv /etc/apache2/sites-available/111-default.conf /etc/apache2/sites-available/000-default.conf
service apache2 restart

# download wordpress
wget -O /tmp/latest.tgz http://wordpress.org/latest.tar.gz

# extract it and fix permissions
cd /var/www
tar xvfz /tmp/latest.tgz > /dev/null
chown -R www-data:www-data /var/www

#-------------------------
# Set MySQL Permissions
#-------------------------

cat <<\EOF>/tmp/setup.mysql
CREATE DATABASE if not exists {{ wordpress_db_name }};
CREATE USER {{ wordpress_db_username }}@'localhost' identified by '{{ wordpress_db_password }}';
GRANT ALL ON wordpress.* to {{ wordpress_db_username }}@'localhost';
FLUSH PRIVILEGES;
EOF

mysql -u root --password='{{ mysql_root_password }}' < /tmp/setup.mysql
rm /tmp/setup.mysql

#-----------------------------
# Create wordpress config file
#-----------------------------

cat <<\EOF>/var/www/wordpress/wp-config.php
<?php
define('DB_NAME',       '{{ wordpress_db_name }}');
define('DB_USER',       '{{ wordpress_db_username }}');
define('DB_PASSWORD',   '{{ wordpress_db_password }}');
define('DB_HOST',       'localhost');
define('DB_CHARSET',    'utf8');
define('DB_COLLATE',    '');

define('AUTH_KEY',         '{{ wordpress_auth_key }}');
define('SECURE_AUTH_KEY',  '{{ wordpress_secure_auth_key }}');
define('LOGGED_IN_KEY',    '{{ wordpress_logged_in_key }}');
define('NONCE_KEY',        '{{ wordpress_nonce_key }}');
define('AUTH_SALT',        '{{ wordpress_auth_salt }}');
define('SECURE_AUTH_SALT', '{{ wordpress_secure_auth_salt }}');
define('LOGGED_IN_SALT',   '{{ wordpress_logged_in_salt }}');
define('NONCE_SALT',       '{{ wordpress_nonce_salt }}');

$table_prefix  = 'wp_';
define('WPLANG', '');
define('WP_DEBUG', false);

/** Absolute path to the WordPress directory. */
if ( !defined('ABSPATH') )
    define('ABSPATH', dirname(__FILE__) . '/');

/** Sets up WordPress vars and included files. */
require_once(ABSPATH . 'wp-settings.php');
EOF

# move temporary wordpress install into '/'
mv /var/www/wordpress/* /var/www
rmdir /var/www/wordpress

# remove temporary index.html giving way to index.php
rm -rf /var/www/index.html

_eof

text_template wordpress_result_template

Your wordpress site is ready at the following URL:

http://{{ wordpress_server.ipaddress_public }}/

_eof
